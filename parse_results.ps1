[xml]$xml = Get-Content 'H:\Work\PhysicsDrivenMovementDemo\TestResults\PlayMode.xml'
$failures = $xml.SelectNodes("//test-case[@result='Failed']")
foreach ($f in $failures) {
    Write-Host "FAIL: $($f.fullname)"
    $msg = $f.failure.message.'#cdata-section'
    Write-Host "  MSG: $($msg -replace "`n",' ')"
    $stack = $f.failure.'stack-trace'.'#cdata-section'
    Write-Host "  STACK: $($stack -replace "`n",' ')"
    Write-Host ""
}
