﻿using Cube;
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
            _characterController.Motor.SetPositionAndRotation(transform.position, transform.rotation);

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

                    var rotation = Quaternion.AngleAxis(Mathf.Lerp(transform.rotation.eulerAngles.y, _serverYaw, a), Vector3.up);

                    _characterController.Motor.MoveCharacter(transform.position + positionCorrection);
                    _characterController.Motor.RotateCharacter(rotation);
                }
            }
        }

        void UpdateInput() {
            Assert.IsNotNull(_pawn.controller);

            var input = _pawn.controller.input;

            var characterInputs = new PlayerCharacterInputs {
                MoveAxisForward = (input.forward ? 1 : 0) - (input.back ? 1 : 0),
                MoveAxisRight = (input.right ? 1 : 0) - (input.left ? 1 : 0),
                JumpDown = input.jump,
                CameraRotation = Quaternion.AngleAxis(input.yaw, Vector3.up),
                CrouchDown = Input.GetKeyDown(KeyCode.C), // #todo
                CrouchUp = Input.GetKeyUp(KeyCode.C)
            };

            _characterController.SetInputs(ref characterInputs);

#if SERVER
            if (isServer) {
                // Snap to client position if close enough, else correct client
                var remotePlayerController = _pawn.controller as ServerRemotePlayerController;
                if (remotePlayerController != null) {
                    var diff = input.position - transform.position;
                    if (diff.sqrMagnitude < 1) {
                        _characterController.Motor.SetPosition(input.position);
                    } else {
                        input.position = transform.position;

                        if (Time.time >= _nextClientPositionCorrectionTime) {
                            _nextClientPositionCorrectionTime = Time.time + 0.3f;

                            _playerControllerSystem.SendControllerResetPawnPosition(remotePlayerController.connection, transform.position);
                        }
                    }
                }
            }
#endif
#if CLIENT
            if (isClient) {
                input.position = transform.position;

                _pawn.controller.input = input;
            }
#endif
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

            if (_pawn.isMounted)
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