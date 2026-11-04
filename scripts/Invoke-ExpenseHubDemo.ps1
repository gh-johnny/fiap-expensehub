[CmdletBinding()]
param(
    [Parameter(Mandatory)][uri]$BaseUrl,
    [string]$OutputDirectory = "artifacts/demo",
    [string]$CommitSha = $env:GITHUB_SHA
)

$ErrorActionPreference = "Stop"
$startedAt = [DateTime]::UtcNow
$scenarios = [System.Collections.Generic.List[object]]::new()
$runId = [Guid]::NewGuid().ToString("N")
$credential = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) + "aA1!"
if ($env:GITHUB_ACTIONS -eq "true") { Write-Output "::add-mask::$credential" }
$today = [DateTime]::UtcNow.ToString("yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture)
$failure = $null

function Assert-That {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw [InvalidOperationException]::new($Message) }
}

function Invoke-Check {
    param(
        [string]$Name, [string]$Method, [string]$Path, [int]$Expected,
        [object]$Body = $null, [string]$AccessToken = $null, [scriptblock]$Verify = $null
    )
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $record = [ordered]@{ name = $Name; expectedStatus = $Expected; observedStatus = $null; passed = $false; durationMs = 0; traceId = $null }
    try {
        $arguments = @{
            Uri = $BaseUrl.AbsoluteUri.TrimEnd('/') + $Path
            Method = $Method
            SkipHttpErrorCheck = $true
            TimeoutSec = 20
            Headers = @{}
        }
        if ($AccessToken) { $arguments.Headers.Authorization = "Bearer " + $AccessToken }
        if ($null -ne $Body) {
            $arguments.ContentType = "application/json"
            $arguments.Body = $Body | ConvertTo-Json -Depth 20 -Compress
        }
        $response = Invoke-WebRequest @arguments
        $record.observedStatus = [int]$response.StatusCode
        $content = if ($response.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($response.Content) } else { [string]$response.Content }
        $data = if ([string]::IsNullOrWhiteSpace($content)) { $null } else { $content | ConvertFrom-Json -Depth 100 -NoEnumerate }
        if ($null -ne $data -and $data.PSObject.Properties.Name -contains "traceId") { $record.traceId = $data.traceId }
        Assert-That ($record.observedStatus -eq $Expected) "$Name returned $($record.observedStatus), expected $Expected."
        if ($Expected -ge 400 -and $Path.StartsWith('/api/', [StringComparison]::Ordinal)) {
            Assert-That ($data.status -eq $Expected -and $data.code -and $data.traceId) "$Name omitted its ProblemDetails contract."
        }
        if ($Verify) { & $Verify $data }
        $record.passed = $true
        return ,$data
    }
    finally {
        $watch.Stop()
        $record.durationMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 2)
        $scenarios.Add([pscustomobject]$record)
    }
}

function New-DemoAccount {
    param([string]$Name, [string[]]$Roles)
    $email = "$Name-$runId@example.test"
    $null = Invoke-Check "$Name register" POST /register 200 @{ email = $email; password = $credential; roles = @("Admin") }
    $users = Invoke-Check "$Name lookup" GET /api/admin/users 200 -AccessToken $adminToken
    $account = @($users | Where-Object email -EQ $email)[0]
    Assert-That ($null -ne $account -and @($account.roles).Count -eq 0) "Registration granted an injected role."
    $old = Invoke-Check "$Name initial login" POST '/login?useCookies=false' 200 @{ email = $email; password = $credential }
    $null = Invoke-Check "$Name role assignment" PUT "/api/admin/users/$($account.id)/roles" 204 @{ roles = $Roles } $adminToken
    $login = Invoke-Check "$Name fresh login" POST '/login?useCookies=false' 200 @{ email = $email; password = $credential }
    Assert-That (-not [string]::IsNullOrWhiteSpace($login.accessToken)) "Login omitted bearer."
    return [pscustomobject]@{ id = $account.id; email = $email; token = $login.accessToken; oldToken = $old.accessToken }
}

function Get-DemoHistory {
    param([string]$Name, [string]$Id, [string]$AccessToken, [int]$Count)
    Invoke-Check $Name GET "/api/expenses/$Id/history" 200 -AccessToken $AccessToken -Verify {
        param($events)
        Assert-That (@($events).Count -eq $Count) "$Name has an unexpected event count."
        for ($index = 0; $index -lt $Count; $index++) {
            Assert-That ($events[$index].revision -eq ($index + 1)) "$Name is not ordered by revision."
            $instant = $events[$index].occurredAtUtc
            $utc = ($instant -is [string] -and $instant.EndsWith('Z', [StringComparison]::Ordinal)) -or ($instant -is [DateTime] -and $instant.Kind -eq [DateTimeKind]::Utc)
            Assert-That $utc "$Name omitted UTC."
        }
    }
}

try {
    Assert-That ($BaseUrl.IsAbsoluteUri -and $BaseUrl.Scheme -in @('http', 'https')) "BaseUrl must be an absolute HTTP URL."
    Assert-That ($env:Seed__AdminEmail -and $env:Seed__AdminPassword) "Provide Admin credentials through Seed__AdminEmail/Seed__AdminPassword."
    $null = Invoke-Check "Health" GET /health 200
    $null = Invoke-Check "OpenAPI contracts" GET /openapi/v1.json 200 -Verify {
        param($document)
        $routes = @(
            @('post', '/register'), @('post', '/login'), @('get', '/api/admin/users'), @('put', '/api/admin/users/{id}/roles'),
            @('post', '/api/expenses'), @('put', '/api/expenses/{id}'), @('get', '/api/expenses'), @('get', '/api/expenses/{id}'),
            @('post', '/api/expenses/{id}/submit'), @('post', '/api/expenses/{id}/approve'), @('post', '/api/expenses/{id}/reject'),
            @('post', '/api/expenses/{id}/pay'), @('get', '/api/expenses/{id}/history')
        )
        $ids = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $routes) {
            $operation = $document.paths.PSObject.Properties[$entry[1]].Value.PSObject.Properties[$entry[0]].Value
            Assert-That ($null -ne $operation -and $operation.operationId -and $ids.Add($operation.operationId)) "Required operation is absent or duplicated."
            if ($entry[1].StartsWith('/api/', [StringComparison]::Ordinal)) {
                Assert-That (@($operation.security).Count -eq 1 -and $null -ne $operation.security[0].Bearer) "Protected operation omitted bearer."
            } else { Assert-That (@($operation.security).Count -eq 0) "Public operation requires bearer." }
        }
        Assert-That ($document.components.securitySchemes.Bearer.type -eq 'http' -and $document.components.securitySchemes.Bearer.scheme -eq 'bearer') "Incorrect security scheme."
        $fields = $document.components.schemas.ExpenseDraftRequest.properties
        Assert-That ($fields.description.minLength -eq 10 -and $fields.description.maxLength -eq 500 -and $fields.amount.minimum -eq 0.01 -and $fields.amount.maximum -eq 2147483647 -and $fields.expenseDate.format -eq 'date') "Incorrect draft schema limits."
    }
    $null = Invoke-Check "Anonymous bearer challenge" GET /api/expenses 401
    $null = Invoke-Check "Invalid bearer challenge" GET /api/expenses 401 -AccessToken "invalid"
    $adminLogin = Invoke-Check "Admin login" POST '/login?useCookies=false' 200 @{ email = $env:Seed__AdminEmail; password = $env:Seed__AdminPassword }
    $adminToken = $adminLogin.accessToken
    $accounts = @{}
    $accounts.owner = New-DemoAccount owner @('Employee')
    $accounts.other = New-DemoAccount other @('Employee')
    $accounts.approver = New-DemoAccount approver @('Approver')
    $accounts.finance = New-DemoAccount finance @('Finance')
    $accounts.auditor = New-DemoAccount auditor @('Auditor')
    $accounts.combined = New-DemoAccount combined @('Employee', 'Approver', 'Finance', 'Auditor', 'Admin')
    $accounts.roleless = New-DemoAccount roleless @()
    $null = Invoke-Check "Old token does not gain roles" GET /api/expenses 403 -AccessToken $accounts.owner.oldToken
    $null = Invoke-Check "Roleless denial" GET /api/expenses 403 -AccessToken $accounts.roleless.token
    $null = Invoke-Check "Admin has no expense access" GET /api/expenses 403 -AccessToken $adminToken
    $null = Invoke-Check "Unknown role is rejected" PUT "/api/admin/users/$($accounts.owner.id)/roles" 400 @{ roles = @('Employee', 'Unknown') } $adminToken
    $null = Invoke-Check "Null role member is rejected" PUT "/api/admin/users/$($accounts.owner.id)/roles" 400 @{ roles = @('Employee', $null) } $adminToken
    $users = Invoke-Check "Admin self lookup" GET /api/admin/users 200 -AccessToken $adminToken
    $adminId = @($users | Where-Object email -EQ $env:Seed__AdminEmail)[0].id
    $null = Invoke-Check "Admin cannot remove own Admin" PUT "/api/admin/users/$adminId/roles" 403 @{ roles = @() } $adminToken
    $draftBody = @{ description = '  Client train ticket expense  '; amount = [decimal]100.01; expenseDate = $today; ownerId = 'forged'; status = 'Paid'; revision = 99 }
    $null = Invoke-Check "Auditor cannot create" POST /api/expenses 403 $draftBody $accounts.auditor.token
    $draft = Invoke-Check "Create server-owned Draft" POST /api/expenses 201 $draftBody $accounts.owner.token -Verify {
        param($expense)
        Assert-That ($expense.ownerId -eq $accounts.owner.id -and $expense.status -eq 'Draft' -and $expense.revision -eq 1 -and $expense.description -eq 'Client train ticket expense' -and $expense.amount -eq 100.01) "Draft accepted forged server fields."
    }
    $expenseId = $draft.id
    $null = Get-DemoHistory "Creation history" $expenseId $accounts.owner.token 1
    $null = Invoke-Check "Other Employee detail invisible" GET "/api/expenses/$expenseId" 404 -AccessToken $accounts.other.token
    $null = Invoke-Check "Other Employee history invisible" GET "/api/expenses/$expenseId/history" 404 -AccessToken $accounts.other.token
    $null = Invoke-Check "Approver cannot read Draft" GET "/api/expenses/$expenseId" 404 -AccessToken $accounts.approver.token
    $null = Invoke-Check "Other Employee cannot edit" PUT "/api/expenses/$expenseId" 403 $draftBody $accounts.other.token
    $editedBody = @{ description = 'Updated client train expense'; amount = [decimal]25.50; expenseDate = $today }
    $null = Invoke-Check "Edit Draft" PUT "/api/expenses/$expenseId" 200 $editedBody $accounts.owner.token
    $editedHistory = Get-DemoHistory "Draft edit snapshots" $expenseId $accounts.owner.token 2
    Assert-That ($editedHistory[1].previousAmount -eq 100.01 -and $editedHistory[1].newAmount -eq 25.50 -and $editedHistory[1].previousDescription -eq 'Client train ticket expense' -and $editedHistory[1].newDescription -eq $editedBody.description) "Edit snapshots were not preserved."
    $null = Invoke-Check "Identical edit is a no-op" PUT "/api/expenses/$expenseId" 200 $editedBody $accounts.owner.token
    $null = Get-DemoHistory "No extra event for identical edit" $expenseId $accounts.owner.token 2
    $invalidCases = @(
        @{ description = 'short'; amount = 1; expenseDate = $today },
        @{ description = ('x' * 501); amount = 1; expenseDate = $today },
        @{ description = 'Invalid amount expense'; amount = 0; expenseDate = $today },
        @{ description = 'Invalid amount expense'; amount = -1; expenseDate = $today },
        @{ description = 'Invalid amount expense'; amount = [decimal]2147483647.01; expenseDate = $today },
        @{ description = 'Missing date expense'; amount = 1 },
        @{ description = 'Invalid date expense'; amount = 1; expenseDate = 'not-a-date' },
        @{ description = 'Future date expense'; amount = 1; expenseDate = [DateTime]::UtcNow.AddDays(1).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture) }
    )
    for ($index = 0; $index -lt $invalidCases.Count; $index++) {
        $null = Invoke-Check "Invalid draft case $index" POST /api/expenses 400 $invalidCases[$index] $accounts.owner.token
    }
    $null = Invoke-Check "Invalid drafts did not persist" GET /api/expenses 200 -AccessToken $accounts.owner.token -Verify {
        param($expenses)
        Assert-That (@($expenses).Count -eq 1 -and $expenses[0].id -eq $expenseId) "Invalid inputs persisted an expense."
    }
    $null = Invoke-Check "Foreign Employee cannot submit" POST "/api/expenses/$expenseId/submit" 403 -AccessToken $accounts.other.token
    $null = Invoke-Check "Submit Draft" POST "/api/expenses/$expenseId/submit" 200 -AccessToken $accounts.owner.token -Verify {
        param($expense)
        Assert-That ($expense.status -eq 'Submitted' -and $expense.revision -eq 3) "Submit did not change the state/revision."
    }
    $null = Invoke-Check "Repeat submit" POST "/api/expenses/$expenseId/submit" 409 -AccessToken $accounts.owner.token
    $null = Invoke-Check "Cannot edit Submitted" PUT "/api/expenses/$expenseId" 409 $editedBody $accounts.owner.token
    $null = Invoke-Check "Approver sees Submitted" GET "/api/expenses/$expenseId" 200 -AccessToken $accounts.approver.token
    $null = Invoke-Check "Finance cannot read Submitted" GET "/api/expenses/$expenseId" 404 -AccessToken $accounts.finance.token
    $null = Invoke-Check "Finance cannot pay Submitted" POST "/api/expenses/$expenseId/pay" 409 -AccessToken $accounts.finance.token
    $null = Invoke-Check "Auditor cannot decide" POST "/api/expenses/$expenseId/approve" 403 -AccessToken $accounts.auditor.token
    $null = Invoke-Check "Approve Submitted" POST "/api/expenses/$expenseId/approve" 200 -AccessToken $accounts.approver.token -Verify {
        param($expense)
        Assert-That ($expense.status -eq 'Approved' -and $expense.revision -eq 4) "Approval did not change the state/revision."
    }
    $null = Invoke-Check "Approver loses read visibility" GET "/api/expenses/$expenseId" 404 -AccessToken $accounts.approver.token
    $null = Invoke-Check "Repeat approval remains conflict" POST "/api/expenses/$expenseId/approve" 409 -AccessToken $accounts.approver.token
    $null = Invoke-Check "Pay Approved" POST "/api/expenses/$expenseId/pay" 200 -AccessToken $accounts.finance.token -Verify {
        param($expense)
        Assert-That ($expense.status -eq 'Paid' -and $expense.revision -eq 5 -and $expense.amount -eq 25.50) "Payment did not preserve the amount/state/revision."
    }
    $null = Invoke-Check "Repeat payment" POST "/api/expenses/$expenseId/pay" 409 -AccessToken $accounts.finance.token
    $paidHistory = Get-DemoHistory "Paid timeline" $expenseId $accounts.owner.token 5
    Assert-That (($paidHistory.action -join ',') -eq 'Created,Updated,Submitted,Approved,Paid' -and $paidHistory[4].actorId -eq $accounts.finance.id) "Paid timeline has incorrect actions/actor."
    $null = Get-DemoHistory "Auditor reads Paid timeline" $expenseId $accounts.auditor.token 5
    $null = Get-DemoHistory "Finance reads Paid timeline" $expenseId $accounts.finance.token 5
    $null = Invoke-Check "Paid is terminal" POST "/api/expenses/$expenseId/submit" 409 -AccessToken $accounts.owner.token
    $rejected = Invoke-Check "Create rejection fixture" POST /api/expenses 201 @{ description = 'Meal receipt expense'; amount = [decimal]0.01; expenseDate = $today } $accounts.owner.token
    $rejectId = $rejected.id
    $null = Invoke-Check "Submit rejection fixture" POST "/api/expenses/$rejectId/submit" 200 -AccessToken $accounts.owner.token
    foreach ($reason in @('short', ('x' * 501), '   ', $null)) {
        $null = Invoke-Check "Invalid rejection reason" POST "/api/expenses/$rejectId/reject" 400 @{ reason = $reason } $accounts.approver.token
    }
    $null = Invoke-Check "Reject Submitted" POST "/api/expenses/$rejectId/reject" 200 @{ reason = '  Receipt is missing  ' } $accounts.approver.token
    $null = Invoke-Check "Repeat rejection" POST "/api/expenses/$rejectId/reject" 409 @{ reason = 'Receipt is missing' } $accounts.approver.token
    $null = Invoke-Check "Rejected is terminal" POST "/api/expenses/$rejectId/submit" 409 -AccessToken $accounts.owner.token
    $rejectHistory = Get-DemoHistory "Rejected timeline" $rejectId $accounts.owner.token 3
    Assert-That ($rejectHistory[2].newStatus -eq 'Rejected' -and $rejectHistory[2].reason -eq 'Receipt is missing' -and $rejectHistory[2].actorId -eq $accounts.approver.id) "Rejected history omitted justification or actor."
    $combined = Invoke-Check "Combined roles can create" POST /api/expenses 201 @{ description = 'Combined profile expense'; amount = [decimal]2147483647; expenseDate = $today } $accounts.combined.token
    $combinedId = $combined.id
    $null = Invoke-Check "Combined roles can submit" POST "/api/expenses/$combinedId/submit" 200 -AccessToken $accounts.combined.token
    $null = Invoke-Check "All roles cannot self-approve" POST "/api/expenses/$combinedId/approve" 403 -AccessToken $accounts.combined.token
    $null = Invoke-Check "All roles cannot self-reject" POST "/api/expenses/$combinedId/reject" 403 @{ reason = 'Receipt is missing' } $accounts.combined.token
    $null = Get-DemoHistory "Denied self-actions added no history" $combinedId $accounts.combined.token 2
    $null = Invoke-Check "Foreign approval of combined owner" POST "/api/expenses/$combinedId/approve" 200 -AccessToken $accounts.approver.token
    $null = Invoke-Check "All roles cannot self-pay" POST "/api/expenses/$combinedId/pay" 403 -AccessToken $accounts.combined.token
    $null = Get-DemoHistory "Denied self-payment added no history" $combinedId $accounts.combined.token 3
    $null = Invoke-Check "Foreign payment of combined owner" POST "/api/expenses/$combinedId/pay" 200 -AccessToken $accounts.finance.token
    $null = Invoke-Check "Accumulate Employee and Approver scopes" GET /api/expenses 200 -AccessToken $accounts.combined.token -Verify {
        param($expenses)
        Assert-That ($expenses.id -contains $combinedId -and $expenses.id -contains $expenseId -and $expenses.id -contains $rejectId) "Auditor union lost visible expenses."
    }
}
catch {
    $failure = $_.Exception.Message
    if ($scenarios.Count -eq 0 -or $scenarios[$scenarios.Count - 1].passed) {
        $scenarios.Add([pscustomobject]@{ name = 'Scenario invariant'; expectedStatus = $null; observedStatus = $null; passed = $false; durationMs = 0; traceId = $null })
    }
}
finally {
    $null = New-Item -ItemType Directory -Path $OutputDirectory -Force
    $report = [ordered]@{
        commit = $(if ([string]::IsNullOrWhiteSpace($CommitSha)) { 'working-tree' } else { $CommitSha })
        runId = $runId
        startedAtUtc = $startedAt.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        finishedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        status = $(if ($failure) { 'failed' } else { 'passed' })
        scenarios = $scenarios
        summary = @{ total = $scenarios.Count; passed = @($scenarios | Where-Object passed).Count; failed = @($scenarios | Where-Object { -not $_.passed }).Count }
    }
    $report | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $OutputDirectory 'report.json') -Encoding utf8
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# ExpenseHub HTTP demonstration')
    $lines.Add('')
    $lines.Add("Commit: $($report.commit). Status: $($report.status). Scenarios: $($report.summary.passed)/$($report.summary.total).")
    $lines.Add('')
    $lines.Add('| Scenario | Expected | Observed | Passed | Duration ms | Trace ID |')
    $lines.Add('|---|---:|---:|:---:|---:|---|')
    foreach ($scenario in $scenarios) {
        $lines.Add("| $($scenario.name) | $($scenario.expectedStatus) | $($scenario.observedStatus) | $($scenario.passed) | $($scenario.durationMs) | $($scenario.traceId) |")
    }
    $lines | Set-Content -Path (Join-Path $OutputDirectory 'report.md') -Encoding utf8
}
if ($failure) { Write-Error "Demo failed: $failure"; exit 1 }
Write-Host "Demo passed: $($scenarios.Count) scenarios. Reports: $OutputDirectory"
