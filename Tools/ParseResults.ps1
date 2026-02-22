param([string]$XmlPath = "H:\Work\PhysicsDrivenMovementDemo\TestResults\PlayMode.xml")

$content = [System.IO.File]::ReadAllText($XmlPath)
$xml = [System.Xml.XmlDocument]::new()
$xml.LoadXml($content)

$run = $xml.'test-run'
Write-Host ("Result=" + $run.result + " Total=" + $run.total + " Passed=" + $run.passed + " Failed=" + $run.failed)

$failures = $xml.SelectNodes("//test-case[@result='Failed']")
foreach ($f in $failures) {
    Write-Host ("FAIL: " + $f.fullname)
    if ($f.failure -and $f.failure.message) {
        Write-Host ("  MSG: " + $f.failure.message.InnerText)
    }
}
