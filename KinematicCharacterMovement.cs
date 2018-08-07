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

        Vector3 _serverPosition;
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
                    var diff = _serverPosition - transform.position;
                    if (diff.sqrMagnitude < 1) {
                        _characterController.Motor.MoveCharacter(_serverPosition);
                    } else {
                        _characterController.Motor.SetPosition(_serverPosition);
                    }

                    var rotation = Quaternion.AngleAxis(_serverYaw, Vector3.up);
                    _characterController.Motor.RotateCharacter(rotation);
                }
            }
        }

        void UpdateInput() {
            Assert.IsNotNull(_pawn.controller);
            
            if (_pawn.playerController != null) {
                var input = _pawn.playerController.input;

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
                    _pawn.playerController.input.worldPosition = transform.position;
                }
#endif
            }
#if SERVER
            if (isServer && _pawn.remotePlayerController != null) {
                // Snap to client position if close enough, else correct client
                var diff = _pawn.remotePlayerController.input.worldPosition - transform.position;
                if (diff.sqrMagnitude < 6) {
                    _characterController.Motor.SetPosition(_pawn.remotePlayerController.input.worldPosition);
                } else {
                    _pawn.remotePlayerController.input.worldPosition = transform.position;

                    if (Time.time >= _nextClientPositionCorrectionTime) {
                        _nextClientPositionCorrectionTime = Time.time + 0.2f;

                        _playerControllerSystem.SendControllerResetPawnPosition(_pawn.remotePlayerController.connection, transform.position);
                    }
                }
            }
#endif
            var aiController = _pawn.controller as AIController;
            if (aiController != null) {
                var input = aiController.input;

                var characterInputs = new AICharacterInputs {
                    MoveVector = input.moveVector,
                    LookVector = input.lookVector,
                    JumpDown = input.jump
                };
                _characterController.SetInputs(ref characterInputs);
            }
        }

        public void CorrectPosition(Vector3 position) {
            _characterController.Motor.SetPosition(position);
        }

        public void AddForce(Vector3 force, ForceMode mode) {
            _characterController.Motor.ForceUnground();
            _characterController.AddVelocity(force);
        }

        public void OnEnterLadder(Ladder ladder) {
        }

        public void OnExitLadder(Ladder ladder) {
        }

#if SERVER
        public override void Serialize(BitStream bs, ReplicaSerializationMode mode, ReplicaView view) {
            var isViewClientControlled = _pawn.isMounted || (_pawn.remotePlayerController != null && _pawn.remotePlayerController.connection == view.connection);
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

            _serverPosition = pos;
            _serverYaw = yaw;
        }
#endif
    }
}