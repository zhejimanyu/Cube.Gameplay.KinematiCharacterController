using Cube;
using Cube.Gameplay;
using Cube.Networking;
using Cube.Networking.Server;
using UnityEngine;
using UnityEngine.Assertions;
using BitStream = Cube.Networking.BitStream;

namespace Pirates {
    [AddComponentMenu("KinematicCharacterMovement")]
    [RequireComponent(typeof(Pawn))]
    [RequireComponent(typeof(KinematicCharacterController))]
    public class KinematicCharacterMovement : ReplicaBehaviour, IPawnMovement {
        ServerPlayerControllerSystem _playerControllerSystem;

        Pawn _pawn;
        KinematicCharacterController _characterController;

        Vector3 _positionCorrection;
        float _serverYaw;

        float _nextClientPositionCorrectionTime;

        void Start() {
            _pawn = GetComponent<Pawn>();

            _characterController = GetComponent<KinematicCharacterController>();

            if (isServer) {
                _playerControllerSystem = gameObject.GetSystem<ServerPlayerControllerSystem>();
            }
        }

        void Update() {
            if (_pawn.controller != null) {
                _pawn.controller.UpdateInput();
                UpdateInput();
            } else {
                if (isClient && !_pawn.isMounted) {
                    var a = Mathf.Min(1, Time.deltaTime * 8);

                    var positionCorrection = _positionCorrection * a;
                    _positionCorrection -= positionCorrection;

                    var rotation = Quaternion.AngleAxis(Mathf.LerpAngle(transform.rotation.eulerAngles.y, _serverYaw, a), Vector3.up);

                    _characterController.Motor.MoveCharacter(transform.position + positionCorrection);
                    _characterController.Motor.RotateCharacter(rotation);
                }
            }
        }

        void UpdateInput() {
            Assert.IsNotNull(_pawn.controller);

            var playerController = _pawn.controller as PlayerController;
            if (playerController != null) {
                var input = playerController.playerInput;

                var characterInputs = new PlayerCharacterInputs {
                    MoveAxisForward = (input.forward ? 1 : 0) - (input.back ? 1 : 0),
                    MoveAxisRight = (input.right ? 1 : 0) - (input.left ? 1 : 0),
                    JumpDown = input.jump,
                    CameraRotation = Quaternion.AngleAxis(input.yaw, Vector3.up),
                    CrouchDown = Input.GetKeyDown(KeyCode.C), // #todo
                    CrouchUp = Input.GetKeyUp(KeyCode.C)
                };
                _characterController.SetInputs(ref characterInputs);

#if CLIENT
                if (isClient) {
                    playerController.playerInput.position = transform.position;
                }
#endif
            }
#if SERVER
            if (isServer) {
                var remotePlayerController = _pawn.controller as ServerRemotePlayerController;
                if (remotePlayerController != null) {
                    // Snap to client position if close enough, else correct client
                    var diff = remotePlayerController.playerInput.position - transform.position;
                    if (diff.sqrMagnitude < 1) {
                        _characterController.Motor.SetPosition(remotePlayerController.playerInput.position);
                    } else {
                        remotePlayerController.playerInput.position = transform.position;

                        if (Time.time >= _nextClientPositionCorrectionTime) {
                            _nextClientPositionCorrectionTime = Time.time + 0.3f;

                            _playerControllerSystem.SendControllerResetPawnPosition(remotePlayerController.connection, transform.position);
                        }
                    }
                }
            }
#endif
            var aiController = _pawn.controller as AIController;
            if (aiController != null) {
                var input = aiController.aiInput;

                var characterInputs = new AICharacterInputs {
                    MoveVector = input.MoveVector,
                    LookVector = input.LookVector
                };
                _characterController.SetInputs(ref characterInputs);
            }
        }

        public void CorrectPosition(Vector3 position) {
            _characterController.Motor.SetPosition(position);
        }

        public void OnEnterLadder(Ladder ladder) {
        }

        public void OnExitLadder(Ladder ladder) {
        }

#if SERVER
        public override void Serialize(BitStream bs, ReplicaSerializationMode mode, ReplicaView view) {
            var remotePlayerController = _pawn.controller as ServerRemotePlayerController;

            var isViewClientControlled = _pawn.isMounted || (remotePlayerController != null && remotePlayerController.connection == view.connection);
            bs.Write(isViewClientControlled);
            if (isViewClientControlled)
                return;

            bs.Write(transform.position);
            bs.Write(transform.rotation.eulerAngles.y);
        }
#endif
#if CLIENT
        public override void Deserialize(BitStream bs, ReplicaSerializationMode mode) {
            var isViewClientControlled = bs.ReadBool();
            if (isViewClientControlled)
                return;

            var pos = bs.ReadVector3();
            var yaw = bs.ReadFloat();

            if (_pawn == null || _pawn.isMounted)
                return;

            // Position
            var posDiff = pos - transform.position;
            if (posDiff.sqrMagnitude < 1) {
                _positionCorrection = posDiff;
            } else {
                transform.position = pos;

                _positionCorrection = Vector3.zero;
            }

            _serverYaw = yaw;
        }
#endif
    }
}