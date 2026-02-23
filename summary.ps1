[xml]$xml = Get-Content 'H:\Work\PhysicsDrivenMovementDemo\TestResults\PlayMode.xml'
$ts = $xml.SelectSingleNode('//test-suite[@type="Assembly"]')
$total = $ts.GetAttribute('total')
$passed = $ts.GetAttribute('passed')
$failed = $ts.GetAttribute('failed')
Write-Host "Total: $total Passed: $passed Failed: $failed"
