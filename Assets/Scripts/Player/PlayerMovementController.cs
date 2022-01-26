using UnityEngine;
using KinematicCharacterController;

public struct PlayerCharacterInputs
{
	public float MoveAxisForward;
	public float MoveAxisRight;
	public Quaternion CameraRotation;
	public bool JumpDown;
}

public enum PlayerMovementState
{
	Default,
	WallRunning
}

public class PlayerMovementController : BaseCharacterController
{
	private Transform _playerCamera;
	private Transform PlayerCamera
	{
		get
		{
			if (_playerCamera == null)
				_playerCamera = GameObject.FindGameObjectWithTag(Helpers.Tags.PlayerCamera).transform;
			return _playerCamera;
		}
	}

	public PlayerMovementState CurrentPlayerMovementState { get; private set; }
	public PlayerMovementState PreviousPlayerMovementState { get; private set; }
	public float TimeEnteredState { get; private set; }
	public float TimeSinceEnteringState { get { return Time.time - TimeEnteredState; } }

	[Header("Stable Movement")]
	public float OrientationSharpness = 10;
	public AnimationCurve GroundAccelerationSpeed;
	public float GroundAccelerationMultiplier = 18;

	[Header("Air Movement")]
	public float AirAccelerationSpeed = 5f;
	public float Drag = 0.2f;

	[Header("Jumping")]
	public float JumpSpeed = 10f;
	public float JumpPreGroundingGraceTime = 0f;
	public float JumpPostGroundingGraceTime = 0.3f;

	[Header("Misc")]
	public Vector3 Gravity = new Vector3(0, -30f, 0);
	public Vector3 GroundedGravity = new Vector3(0, -15f, 0);
	public Transform MeshRoot;

	public Vector3 _moveInputVector;
	public Vector3 _lookInputVector;
	private bool _jumpRequested = false;
	private bool _jumpConsumed = false;
	private bool _jumpedThisFrame = false;
	private float _timeSinceJumpRequested = Mathf.Infinity;
	private float _timeSinceLastAbleToJump = 0f;
	private bool _canWallJump = false;
	private bool _wallSlideRequested = false;
	private bool _canWallSlide = false;
	private Vector3 _wallJumpNormal;
	private Vector3 _internalVelocityAdd = Vector3.zero;

	private void Start()
	{
		TransitionToState(PlayerMovementState.Default);
	}

	/// <summary>
	/// Handles movement state transitions and enter/exit callbacks
	/// </summary>
	public void TransitionToState(PlayerMovementState newState)
	{
		PreviousPlayerMovementState = CurrentPlayerMovementState;
		OnStateExit(PreviousPlayerMovementState, newState);
		CurrentPlayerMovementState = newState;
		TimeEnteredState = Time.time;
		OnStateEnter(newState, PreviousPlayerMovementState);
	}

	/// <summary>
	/// Event when entering a state
	/// </summary>
	public void OnStateEnter(PlayerMovementState state, PlayerMovementState fromState)
	{
		switch (state)
		{
			case PlayerMovementState.Default:
				{
					break;
				}
		}
	}

	/// <summary>
	/// Event when exiting a state
	/// </summary>
	public void OnStateExit(PlayerMovementState state, PlayerMovementState toState)
	{
		switch (state)
		{
			case PlayerMovementState.Default:
				{
					break;
				}
		}
	}

	/// <summary>
	/// This is called every frame by MyPlayer in order to tell the character what its inputs are
	/// </summary>
	public void SetInputs(ref PlayerCharacterInputs inputs)
	{
		// Clamp input
		Vector3 moveInputVector = Vector3.ClampMagnitude(new Vector3(inputs.MoveAxisRight, 0f, inputs.MoveAxisForward), 1f);

		// Calculate camera direction and rotation on the character plane
		Vector3 cameraPlanarDirection = Vector3.ProjectOnPlane(inputs.CameraRotation * Vector3.forward, Motor.CharacterUp).normalized;
		if (cameraPlanarDirection.sqrMagnitude == 0f)
		{
			cameraPlanarDirection = Vector3.ProjectOnPlane(inputs.CameraRotation * Vector3.up, Motor.CharacterUp).normalized;
		}
		Quaternion cameraPlanarRotation = Quaternion.LookRotation(cameraPlanarDirection, Motor.CharacterUp);

		// Move and look inputs
		_moveInputVector = cameraPlanarRotation * moveInputVector;
		_lookInputVector = cameraPlanarDirection;

		// Jumping input
		if (inputs.JumpDown)
		{
			_timeSinceJumpRequested = 0f;
			_jumpRequested = true;
			_wallSlideRequested = true;
		}
	}

	/// <summary>
	/// (Called by KinematicCharacterMotor during its update cycle)
	/// This is called before the character begins its movement update
	/// </summary>
	public override void BeforeCharacterUpdate(float deltaTime)
	{
	}

	/// <summary>
	/// (Called by KinematicCharacterMotor during its update cycle)
	/// This is where you tell your character what its rotation should be right now. 
	/// This is the ONLY place where you should set the character's rotation
	/// </summary>
	public override void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
	{
		if (_lookInputVector != Vector3.zero && OrientationSharpness > 0f)
		{
			// Smoothly interpolate from current to target look direction
			Vector3 smoothedLookInputDirection = Vector3.Slerp(Motor.CharacterForward, _lookInputVector, 1 - Mathf.Exp(-OrientationSharpness * deltaTime)).normalized;

			// Set the current rotation (which will be used by the KinematicCharacterMotor)
			currentRotation = Quaternion.LookRotation(smoothedLookInputDirection, Motor.CharacterUp);
		}

		// Rotate from current up to invert gravity
		if (Motor.GroundingStatus.IsStableOnGround)
			currentRotation = Quaternion.Lerp(currentRotation, Quaternion.FromToRotation(Vector3.up, Motor.GroundingStatus.OuterGroundNormal), 0.2f);
		else
			currentRotation = Quaternion.Lerp(currentRotation, Quaternion.FromToRotation(Vector3.up, -Gravity), 0.1f);
	}

	/// <summary>
	/// (Called by KinematicCharacterMotor during its update cycle)
	/// This is where you tell your character what its velocity should be right now. 
	/// This is the ONLY place where you can set the character's velocity
	/// </summary>
	public override void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
	{
		if (Motor.GroundingStatus.IsStableOnGround)
		{
			// Reorient velocity on slope
			currentVelocity = Motor.GetDirectionTangentToSurface(currentVelocity, Motor.GroundingStatus.GroundNormal) * currentVelocity.magnitude;

			// Calculate target velocity
			Vector3 inputRight = Vector3.Cross(_moveInputVector, Motor.CharacterUp);
			Vector3 reorientedInput = Vector3.Cross(Motor.GroundingStatus.GroundNormal, inputRight).normalized * _moveInputVector.magnitude;
			currentVelocity += reorientedInput * GroundAccelerationSpeed.Evaluate(currentVelocity.magnitude / GroundAccelerationMultiplier) * GroundAccelerationMultiplier * deltaTime;

			// Gravity
			Vector3 gravityComponent = (Vector3.Cross(Vector3.Cross(Motor.GroundingStatus.GroundNormal, Gravity), Motor.GroundingStatus.GroundNormal)).normalized * GroundedGravity.magnitude;
			currentVelocity += gravityComponent * deltaTime;

			// Drag
			currentVelocity *= (1f / (1f + (Drag * deltaTime))); //Drag should be based on speed as well - Moving faster means more drag
		}
		else
		{
			// Add move input
			if (_moveInputVector.sqrMagnitude > 0f)
			{
				currentVelocity += _moveInputVector * AirAccelerationSpeed * deltaTime;
			}

			// Gravity
			currentVelocity += Gravity * deltaTime;

			// Drag
			currentVelocity *= (1f / (1f + (Drag * deltaTime)));
		}

		// Handle jumping
		{
			_jumpedThisFrame = false;
			_timeSinceJumpRequested += deltaTime;
			if (_jumpRequested)
			{
				// See if we actually are allowed to jump
				if (_canWallJump ||
					(!_jumpConsumed && ((Motor.GroundingStatus.FoundAnyGround) || _timeSinceLastAbleToJump <= JumpPostGroundingGraceTime)))
				{
					// Calculate jump direction before ungrounding
					Vector3 jumpDirection = Motor.CharacterUp;
					if (_canWallJump)
					{
						jumpDirection = (PlayerCamera.forward + PlayerCamera.up).normalized;
					}
					else if (Motor.GroundingStatus.FoundAnyGround && !Motor.GroundingStatus.IsStableOnGround)
					{
						jumpDirection = Motor.GroundingStatus.GroundNormal;
					}

					// Makes the character skip ground probing/snapping on its next update. 
					// If this line weren't here, the character would remain snapped to the ground when trying to jump. Try commenting this line out and see.
					Motor.ForceUnground();

					// Add to the return velocity and reset jump state
					currentVelocity += (jumpDirection * JumpSpeed) - Vector3.Project(currentVelocity, Motor.CharacterUp);
					_jumpRequested = false;
					_jumpConsumed = true;
					_jumpedThisFrame = true;
				}
			}

			// Reset wall jump
			_canWallJump = false;
		}

		// Take into account additive velocity
		if (_internalVelocityAdd.sqrMagnitude > 0f)
		{
			currentVelocity += _internalVelocityAdd;
			_internalVelocityAdd = Vector3.zero;
		}
	}

	/// <summary>
	/// (Called by KinematicCharacterMotor during its update cycle)
	/// This is called after the character has finished its movement update
	/// </summary>
	public override void AfterCharacterUpdate(float deltaTime)
	{
		// Handle jump-related values
		{
			// Handle jumping pre-ground grace period
			if (_jumpRequested && _timeSinceJumpRequested > JumpPreGroundingGraceTime)
			{
				_jumpRequested = false;
			}

			if (Motor.GroundingStatus.FoundAnyGround)
			{
				// If we're on a ground surface, reset jumping values
				if (!_jumpedThisFrame)
				{
					_jumpConsumed = false;
				}
				_timeSinceLastAbleToJump = 0f;
			}
			else
			{
				// Keep track of time since we were last able to jump (for grace period)
				_timeSinceLastAbleToJump += deltaTime;
			}
		}

		//Handle gravity-related values
		{
			if (Vector3.Angle(Motor.GroundingStatus.GroundNormal, Gravity) <= 70 && Motor.Velocity.y < -2f)
				Motor.ForceUnground();
		}
	}

	public override bool IsColliderValidForCollisions(Collider coll)
	{
		return true;
	}

	public override void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
	{
	}

	public override void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport)
	{
		// If we request the wall-slide, we should clamp to the wall, and be able to jump off again. There should be a timer for this.
		// TODO: Create a State Machine for the player movement
		//if ()
		// We can wall jump only if we are not stable on ground and are moving against an obstruction
		if (!Motor.GroundingStatus.IsStableOnGround && !hitStabilityReport.IsStable)
		{
			_canWallJump = true;
			_wallJumpNormal = hitNormal;
		}
	}

	public override void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport)
	{
	}

	public override void PostGroundingUpdate(float deltaTime)
	{
	}

	public void AddVelocity(Vector3 velocity)
	{
		_internalVelocityAdd += velocity;
	}

	private void OnDrawGizmos()
	{
		Gizmos.color = Color.green;
		//Gizmos.DrawWireSphere(Motor.GroundingStatus.GroundPoint, 0.1f);
		//Gizmos.DrawRay(Motor.GroundingStatus.GroundPoint, Motor.GroundingStatus.GroundNormal);
	}
}