using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CustomAvatar
{
	public class PlayerAvatarManager
	{
		private readonly AvatarLoader _avatarLoader;
		private readonly PlayerAvatarInput _playerAvatarInput;
		private SpawnedAvatar _currentSpawnedPlayerAvatar;
		private float _prevPlayerHeight = MainSettingsModel.kDefaultPlayerHeight;
		private Vector3 _startAvatarLocalScale = Vector3.one;
		private float? _currentAvatarArmLength = null;
		private Vector3? _startPlatformPosition = null;
		private float? _startAvatarPositionY = null;

		public event Action<CustomAvatar> AvatarChanged;

		private CustomAvatar CurrentPlayerAvatar
		{
			get { return _currentSpawnedPlayerAvatar?.CustomAvatar; }
			set
			{
				if (value == null) return;
				if (CurrentPlayerAvatar == value) return;
				value.Load(CustomAvatarLoaded);
			}
		}

		public PlayerAvatarManager(AvatarLoader avatarLoader, CustomAvatar startAvatar = null)
		{
			_playerAvatarInput = new PlayerAvatarInput();
			_avatarLoader = avatarLoader;

			if (startAvatar != null)
			{
				CurrentPlayerAvatar = startAvatar;
			}

			Plugin.Instance.FirstPersonEnabledChanged += OnFirstPersonEnabledChanged;
			SceneManager.sceneLoaded += SceneManagerOnSceneLoaded;
		}

		~PlayerAvatarManager()
		{
			Plugin.Instance.FirstPersonEnabledChanged -= OnFirstPersonEnabledChanged;
			SceneManager.sceneLoaded -= SceneManagerOnSceneLoaded;
		}

		public CustomAvatar GetCurrentAvatar()
		{
			return CurrentPlayerAvatar;
		}

		public void SwitchToAvatar(CustomAvatar customAvatar)
		{
			CurrentPlayerAvatar = customAvatar;
		}

		public CustomAvatar SwitchToNextAvatar()
		{
			var avatars = _avatarLoader.Avatars;
			if (avatars.Count == 0) return null;

			if (CurrentPlayerAvatar == null)
			{
				CurrentPlayerAvatar = avatars[0];
				return avatars[0];
			}

			var currentIndex = _avatarLoader.IndexOf(CurrentPlayerAvatar);
			if (currentIndex < 0) currentIndex = 0;

			var nextIndex = currentIndex + 1;
			if (nextIndex >= avatars.Count)
			{
				nextIndex = 0;
			}

			var nextAvatar = avatars[nextIndex];
			CurrentPlayerAvatar = nextAvatar;
			return nextAvatar;
		}

		public CustomAvatar SwitchToPreviousAvatar()
		{
			var avatars = _avatarLoader.Avatars;
			if (avatars.Count == 0) return null;

			if (CurrentPlayerAvatar == null)
			{
				CurrentPlayerAvatar = avatars[0];
				return avatars[0];
			}

			var currentIndex = _avatarLoader.IndexOf(CurrentPlayerAvatar);
			if (currentIndex < 0) currentIndex = 0;

			var nextIndex = currentIndex - 1;
			if (nextIndex < 0)
			{
				nextIndex = avatars.Count - 1;
			}

			var nextAvatar = avatars[nextIndex];
			CurrentPlayerAvatar = nextAvatar;
			return nextAvatar;
		}

		private void CustomAvatarLoaded(CustomAvatar loadedAvatar, AvatarLoadResult result)
		{
			if (result != AvatarLoadResult.Completed)
			{
				Plugin.Log("Avatar " + loadedAvatar.FullPath + " failed to load");
				return;
			}

			Plugin.Log("Loaded avatar " + loadedAvatar.Name + " by " + loadedAvatar.AuthorName);

			if (_currentSpawnedPlayerAvatar?.GameObject != null)
			{
				Object.Destroy(_currentSpawnedPlayerAvatar.GameObject);
			}

			_currentSpawnedPlayerAvatar = AvatarSpawner.SpawnAvatar(loadedAvatar, _playerAvatarInput);

			if (AvatarChanged != null)
			{
				AvatarChanged(loadedAvatar);
			}

			_startAvatarLocalScale = _currentSpawnedPlayerAvatar.GameObject.transform.localScale;
			_startAvatarPositionY = null;
			_currentAvatarArmLength = null;
			_prevPlayerHeight = -1;
			FixAvatar();
			ResizePlayerAvatar();

			OnFirstPersonEnabledChanged(Plugin.Instance.FirstPersonEnabled);
		}

		private void OnFirstPersonEnabledChanged(bool firstPersonEnabled)
		{
			if (_currentSpawnedPlayerAvatar == null) return;
			AvatarLayers.SetChildrenToLayer(_currentSpawnedPlayerAvatar.GameObject,
				firstPersonEnabled ? 0 : AvatarLayers.OnlyInThirdPerson);

			foreach (var ex in _currentSpawnedPlayerAvatar.GameObject.GetComponentsInChildren<AvatarScriptPack.FirstPersonExclusion>())
				ex.OnFirstPersonEnabledChanged(firstPersonEnabled);
		}

		private void SceneManagerOnSceneLoaded(Scene newScene, LoadSceneMode mode)
		{
			ResizePlayerAvatar();
			OnFirstPersonEnabledChanged(Plugin.Instance.FirstPersonEnabled);
			_currentSpawnedPlayerAvatar?.GameObject.GetComponentInChildren<AvatarEventsPlayer>()?.Restart();
		}

		private const string PlayerArmLengthKey = "AvatarAutoFitting.PlayerArmLength";
		private const string PlayerViewPointYKey = "AvatarAutoFitting.PlayerViewPointY";
		private const string PlayerGripAngleKey = "AvatarAutoFitting.PlayerGripAngle";
		private float PlayerDefaultViewPointY = BeatSaberUtil.GetPlayerHeight() - 0.11f;
		private float PlayerDefaultArmLength = BeatSaberUtil.GetPlayerHeight() * 0.92f;

		private void ResizePlayerAvatar(float gripRotDelta = 0.0f)
		{
			if (_currentSpawnedPlayerAvatar?.GameObject == null) return;
			if (!_currentSpawnedPlayerAvatar.CustomAvatar.AllowHeightCalibration) return;

			float playerArmLength = PlayerPrefs.GetFloat(PlayerArmLengthKey, PlayerDefaultArmLength);
			float playerViewPointY = PlayerPrefs.GetFloat(PlayerViewPointYKey, PlayerDefaultViewPointY);

			_currentAvatarArmLength = _currentAvatarArmLength ?? AvatarMeasurement.MeasureArmLength(_currentSpawnedPlayerAvatar.GameObject);
			var avatarArmLength = _currentAvatarArmLength ?? playerArmLength;
			Plugin.Log("Avatar Arm Length: " + avatarArmLength);

			var avatarViewPointY = _currentSpawnedPlayerAvatar.CustomAvatar.ViewPoint?.position.y ?? playerViewPointY;

			// fbx root gameobject
			var animator = _currentSpawnedPlayerAvatar.GameObject.GetComponentInChildren<Animator>();
			if (animator == null) { Plugin.Log("Animator not found"); return; }

			// scale
			var scale = playerArmLength / avatarArmLength;
			_currentSpawnedPlayerAvatar.GameObject.transform.localScale = _startAvatarLocalScale * scale;

			// translate root for floor level
			_startAvatarPositionY = _startAvatarPositionY ?? animator.transform.position.y;
			const float FloorLevelOffset = 0.04f; // a heuristic value from testing on oculus rift
			var offset = (playerViewPointY - (avatarViewPointY * scale)) + FloorLevelOffset;
			animator.transform.position = new Vector3(animator.transform.position.x, offset + _startAvatarPositionY ?? 0, animator.transform.position.z);

			// translate platform
			var customFloor = GameObject.Find("Platform Loader");
			if (customFloor != null)
			{
				_startPlatformPosition = _startPlatformPosition ?? customFloor.transform.position;
				var mender = customFloor.AddComponent<FloorLevelMender>();
				mender.destination = (Vector3.up * offset) + _startPlatformPosition ?? Vector3.zero;
			}

			Plugin.Log("Avatar fitted with scale: " + scale + " yoffset: " + offset);

			if (gripRotDelta != 0.0f)
			{
				void rotateGrip(Transform handObject, float rotZ)
				{
					if (handObject == null) return;
					var handTarget = handObject.GetChild(0);
					if (handTarget == null) return;

					handTarget.parent = null;
					var eulerrot = handObject.localRotation.eulerAngles;
					handObject.localRotation = Quaternion.Euler(eulerrot.x, eulerrot.y, eulerrot.z + rotZ);
					handTarget.parent = handObject;
					Plugin.Log("Grip rotation: " + rotZ);
				}
				rotateGrip(_currentSpawnedPlayerAvatar.GameObject.transform.Find("RightHand"), gripRotDelta);
				rotateGrip(_currentSpawnedPlayerAvatar.GameObject.transform.Find("LeftHand"), -gripRotDelta);
			}

		}

		private void FixAvatar()
		{
			var animator = _currentSpawnedPlayerAvatar.GameObject.GetComponentInChildren<Animator>();
			if (animator != null && animator.avatar && animator.isHuman)
			{
				// only z axis alignment
				void fixGrip(HumanBodyBones wristBoneName, HumanBodyBones elbowBoneName, Transform handObject, Vector3 baseArmDirection, float alignRotZ)
				{
					if (handObject == null) return;
					var handTarget = handObject.GetChild(0);
					if (handTarget == null) return;

					var wrist = animator.GetBoneTransform(wristBoneName);
					var elbow = animator.GetBoneTransform(elbowBoneName);

					var rotationToBasePose = Quaternion.FromToRotation(wrist.position - elbow.position, baseArmDirection);
					var rotationToHandObject = Quaternion.FromToRotation(handObject.position - wrist.position, wrist.position - elbow.position);
					var baseRotZ = rotationToBasePose.eulerAngles.z;
					var objectRotZ =  rotationToHandObject.eulerAngles.z;
					var targetDiffRotZ = wrist.rotation.eulerAngles.z - handTarget.rotation.eulerAngles.z;
					baseRotZ -= (baseRotZ > 180.0f) ? 360.0f : 0;
					objectRotZ -= (objectRotZ > 180.0f) ? 360.0f : 0;
					handTarget.parent = null;
					var eulerrot = handObject.localRotation.eulerAngles;
					handObject.localRotation = Quaternion.Euler(eulerrot.x, eulerrot.y, alignRotZ - baseRotZ - objectRotZ + targetDiffRotZ);
					handTarget.parent = handObject;
					Plugin.Log("Grip alignment: " + alignRotZ + " - " + baseRotZ + " - " + objectRotZ + " + " + targetDiffRotZ);
				}

				var rotZ = PlayerPrefs.GetFloat(PlayerGripAngleKey, 95.0f);
				fixGrip(HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm,
					_currentSpawnedPlayerAvatar.GameObject.transform.Find("RightHand"), new Vector3(1f, 0f, 0f), rotZ);
				fixGrip(HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm,
					_currentSpawnedPlayerAvatar.GameObject.transform.Find("LeftHand"), new Vector3(-1f, 0f, 0f), -rotZ);
			}
			// inject late fixer
			_currentSpawnedPlayerAvatar.GameObject.AddComponent<IKSolverMender>();
		}

		public void MeasurePlayerViewPoint()
		{
			var viewPointY = _playerAvatarInput.HeadPosRot.Position.y;
			Plugin.Log("Player ViewPointY: " + viewPointY);
			PlayerPrefs.SetFloat(PlayerViewPointYKey, viewPointY);
			PlayerPrefs.Save();
			ResizePlayerAvatar();
		}

		public void IncrementPlayerArmLength(int step)
		{
			var v = PlayerPrefs.GetFloat(PlayerArmLengthKey, PlayerDefaultArmLength);
			v += 0.05f * step;
			PlayerPrefs.SetFloat(PlayerArmLengthKey, v);
			PlayerPrefs.Save();
			ResizePlayerAvatar();
		}

		public void IncrementPlayerGripAngle(int step)
		{
			var v = PlayerPrefs.GetFloat(PlayerGripAngleKey, 95.0f);
			v += 5.0f * step;
			PlayerPrefs.SetFloat(PlayerGripAngleKey, v);
			PlayerPrefs.Save();
			ResizePlayerAvatar(5.0f * step);
		}

		private class FloorLevelMender : MonoBehaviour
		{
			public Vector3 destination;

			private void LateFix()
			{
				transform.position = destination;
				Plugin.Log("Custom Platform moved to: " + transform.position.y);
				Destroy(this);
			}

			private void Start()
			{
				Invoke("LateFix", 0.1f);
			}
		}

		private class IKSolverMender : MonoBehaviour
		{
			private void LateFix()
			{
				var vrik = GetComponentInChildren<AvatarScriptPack.VRIK>();
				if (vrik == null) return;
				// force plant feet feature disabled and you can jump
				vrik.solver.plantFeet = false;
				var animator = GetComponentInChildren<Animator>();
				if (animator != null && animator.avatar && animator.isHuman)
				{
					// leg bending fix : insert a bend goal 
					void fixLegBend(HumanBodyBones hipBoneName, HumanBodyBones legBoneName, AvatarScriptPack.IKSolverVR.Leg legSolver)
					{
						var hip = animator.GetBoneTransform(hipBoneName);
						var leg = animator.GetBoneTransform(legBoneName);
						if (hip != null && leg != null)
						{
							var bendGoal = new GameObject();
							bendGoal.transform.SetParent(hip);
							bendGoal.transform.localPosition = Vector3.forward + (leg.position - hip.position);
							legSolver.bendGoal = bendGoal.transform;
							legSolver.bendGoalWeight = 1.0f;
							legSolver.swivelOffset = 0f;
						}
					}
					fixLegBend(HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, vrik.solver.leftLeg);
					fixLegBend(HumanBodyBones.Hips, HumanBodyBones.RightUpperLeg, vrik.solver.rightLeg);

					Plugin.Log("Leg bending fix applied.");
				}
			}

			private void Start()
			{
				// override values after the start of IKManagerAdvanced
				Invoke("LateFix", 0.1f);
			}
		}
	}
}
