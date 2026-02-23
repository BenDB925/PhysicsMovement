const fs = require('fs');
const xml = fs.readFileSync('H:/Work/PhysicsDrivenMovementDemo/TestResults/PlayMode.xml', 'utf8');
// Find failure messages
const failureRegex = /<test-case[^>]*result="Failed"[^>]*>([\s\S]*?)<\/test-case>/g;
let match;
while ((match = failureRegex.exec(xml)) !== null) {
    const nameMatch = match[0].match(/name="([^"]+)"/);
    const msgMatch = match[0].match(/<message><!\[CDATA\[([\s\S]*?)\]\]><\/message>/);
    const name = nameMatch ? nameMatch[1] : 'unknown';
    const msg = msgMatch ? msgMatch[1].substring(0, 1500) : 'no message';
    console.log('FAILED: ' + name);
    console.log('MSG: ' + msg);
    console.log('---');
}
