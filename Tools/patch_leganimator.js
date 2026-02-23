const fs = require('fs');
let content = fs.readFileSync('H:/Work/PhysicsDrivenMovementDemo/Assets/Scripts/Character/LegAnimator.cs', 'utf8');

// ─── EDIT 1: Add SerializeField recovery inspector fields after _angularVelocityGaitThreshold ───

const anchor1 = '        private float _angularVelocityGaitThreshold = 8f;\r\n\r\n        // \u2500\u2500 Private Fields ';
if (!content.includes(anchor1)) {
    console.error('ERROR: Could not find anchor 1');
    process.exit(1);
}
const newInspectorFields = [
    '        private float _angularVelocityGaitThreshold = 8f;',
    '',
    '        // \u2500\u2500 Stuck-Leg Recovery (Option D) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500',
    '',
    '        [SerializeField]',
    '        [Tooltip("Number of consecutive FixedUpdate frames where stuck conditions must be met " +',
    '                 "before recovery pose is triggered. Default 25.")]',
    '        private int _stuckFrameThreshold = 25;',
    '',
    '        [SerializeField]',
    '        [Tooltip("Horizontal speed (m/s) below which the character is considered stuck when " +',
    '                 "input is also non-zero. Default 0.15 m/s.")]',
    '        private float _stuckSpeedThreshold = 0.15f;',
    '',
    '        [SerializeField]',
    '        [Tooltip("Number of FixedUpdate frames to hold the recovery pose before resuming " +',
    '                 "normal gait. Default 30.")]',
    '        private int _recoveryFrames = 30;',
    '',
    '        [SerializeField]',
    '        [Tooltip("Spring multiplier applied to both upper-leg joints during recovery to drive " +',
    '                 "the legs forcefully into the forward-split pose. Default 2.5.")]',
    '        private float _recoverySpringMultiplier = 2.5f;',
    '',
    '        // \u2500\u2500 Private Fields ',
].join('\r\n');

content = content.replace(anchor1, newInspectorFields);
console.log('Edit 1: inspector fields inserted');

// ─── EDIT 2: Add private state fields after _spinSuppressFrames ───

const anchor2 = '        private int _spinSuppressFrames;\r\n\r\n        // \u2500\u2500 Public Properties ';
if (!content.includes(anchor2)) {
    console.error('ERROR: Could not find anchor 2');
    process.exit(1);
}

const newPrivateFields = [
    '        private int _spinSuppressFrames;',
    '',
    '        // \u2500\u2500 Stuck-Leg Recovery \u2014 Runtime State \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500',
    '',
    '        /// <summary>Counts consecutive frames where stuck conditions are all true.</summary>',
    '        private int _stuckFrameCounter;',
    '',
    '        /// <summary>True while the recovery pose is being actively applied.</summary>',
    '        private bool _isRecovering;',
    '',
    '        /// <summary>Counts FixedUpdate frames remaining in the current recovery window.</summary>',
    '        private int _recoveryFrameCounter;',
    '',
    '        // \u2500\u2500 Public Properties ',
].join('\r\n');

content = content.replace(anchor2, newPrivateFields);
console.log('Edit 2: private state fields inserted');

// ─── EDIT 3: Add IsRecovering public property after SmoothedInputMag ───

const anchor3 = '        public float SmoothedInputMag => _smoothedInputMag;\r\n\r\n        /// <summary>\r\n        /// Last world-space swing axis';
if (!content.includes(anchor3)) {
    console.error('ERROR: Could not find anchor 3');
    process.exit(1);
}

const newPublicProp = [
    '        public float SmoothedInputMag => _smoothedInputMag;',
    '',
    '        /// <summary>',
    '        /// True while the stuck-leg recovery pose is being actively applied.',
    '        /// Exposed for test verification; read-only at runtime.',
    '        /// </summary>',
    '        public bool IsRecovering => _isRecovering;',
    '',
    '        /// <summary>',
    '        /// Last world-space swing axis',
].join('\r\n');

content = content.replace(anchor3, newPublicProp);
console.log('Edit 3: IsRecovering property inserted');

// ─── EDIT 4: Insert recovery logic in FixedUpdate after _wasMoving=isMoving and before if(isMoving) ───

const anchor4 = '            _prevInputDir = currentInputDir;\r\n            _wasMoving    = isMoving;\r\n\r\n            if (isMoving)\r\n            {';
if (!content.includes(anchor4)) {
    console.error('ERROR: Could not find anchor 4');
    process.exit(1);
}

const newGaitSection = [
    '            _prevInputDir = currentInputDir;',
    '            _wasMoving    = isMoving;',
    '',
    '            // \u2500\u2500 STEP 3E: Stuck-Leg Recovery (Option D) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500',
    '            //   Detection: character is stuck when ALL of the following are true for',
    '            //   _stuckFrameThreshold consecutive frames:',
    '            //     - SmoothedInputMag > 0.5 (actively trying to move)',
    '            //     - horizontalSpeedGate < _stuckSpeedThreshold (not actually moving)',
    '            //     - CharacterState is Standing or Moving (not Fallen/GettingUp/Airborne)',
    '            //   Recovery: drive both UpperLeg joints to forward-split pose (AngleAxis(-30f, _swingAxis))',
    '            //   with spring multiplier _recoverySpringMultiplier for _recoveryFrames frames,',
    '            //   then restore spring and resume normal gait.',
    '',
    '            bool stateAllowsRecovery = state == CharacterStateType.Standing ||',
    '                                       state == CharacterStateType.Moving;',
    '',
    '            if (!_isRecovering)',
    '            {',
    '                // Update stuck counter.',
    '                bool stuckCondition = _smoothedInputMag > 0.5f',
    '                    && horizontalSpeedGate < _stuckSpeedThreshold',
    '                    && stateAllowsRecovery;',
    '',
    '                if (stuckCondition)',
    '                {',
    '                    _stuckFrameCounter++;',
    '                }',
    '                else',
    '                {',
    '                    _stuckFrameCounter = 0;',
    '                }',
    '',
    '                // Trigger recovery when stuck long enough.',
    '                if (_stuckFrameCounter >= _stuckFrameThreshold && stateAllowsRecovery)',
    '                {',
    '                    _isRecovering = true;',
    '                    _recoveryFrameCounter = _recoveryFrames;',
    '                    _stuckFrameCounter = 0;',
    '                    SetLegSpringMultiplier(_recoverySpringMultiplier);',
    '                }',
    '            }',
    '',
    '            if (_isRecovering)',
    '            {',
    '                // Apply forward-split recovery pose to both UpperLeg joints.',
    '                Quaternion recoveryPose = Quaternion.AngleAxis(-30f, _swingAxis);',
    '                if (_upperLegL != null) { _upperLegL.targetRotation = recoveryPose; }',
    '                if (_upperLegR != null) { _upperLegR.targetRotation = recoveryPose; }',
    '',
    '                _recoveryFrameCounter--;',
    '                if (_recoveryFrameCounter <= 0)',
    '                {',
    '                    // Recovery complete: restore spring and resume normal gait.',
    '                    _isRecovering = false;',
    '                    _stuckFrameCounter = 0;',
    '                    SetLegSpringMultiplier(1f);',
    '                }',
    '',
    '                // Skip normal gait this frame \u2014 recovery pose is already applied.',
    '                return;',
    '            }',
    '',
    '            // \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500',
    '            if (isMoving)',
    '            {',
].join('\r\n');

content = content.replace(anchor4, newGaitSection);
console.log('Edit 4: recovery logic in FixedUpdate inserted');

fs.writeFileSync('H:/Work/PhysicsDrivenMovementDemo/Assets/Scripts/Character/LegAnimator.cs', content, 'utf8');
console.log('All edits written successfully.');
