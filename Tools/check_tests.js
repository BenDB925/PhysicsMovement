const fs = require('fs');
const content = fs.readFileSync('H:/Work/PhysicsDrivenMovementDemo/Assets/Tests/PlayMode/Character/LegAnimatorTests.cs', 'utf8');
const lines = content.split('\n');
console.log('Total lines: ' + lines.length);
console.log('Last 30 lines:');
for (let i = lines.length - 30; i < lines.length; i++) {
    if (lines[i] !== undefined) {
        console.log(i + ': ' + JSON.stringify(lines[i].substring(0, 60)));
    }
}
