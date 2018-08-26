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
		private float _currentAvatarOffsetY = 0f;
		private float? _currentAvatarArmLength = null;
		private float _currentPlatformOffsetY = 0f;

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
			_currentAvatarOffsetY = 0f;
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
		}

		private void SceneManagerOnSceneLoaded(Scene newScene, LoadSceneMode mode)
		{
			ResizePlayerAvatar();
			OnFirstPersonEnabledChanged(Plugin.Instance.FirstPersonEnabled);
		}

		private const string PlayerArmLengthKey = "AvatarAutoFitting.PlayerArmLength";
		private const string PlayerViewPointYKey = "AvatarAutoFitting.PlayerViewPointY";
		private float PlayerDefaultViewPortY = BeatSaberUtil.GetPlayerHeight() - 0.11f;
		private float PlayerDefaultArmLength = BeatSaberUtil.GetPlayerHeight() * 0.92f;

		private void ResizePlayerAvatar()
		{
			if (_currentSpawnedPlayerAvatar?.GameObject == null) return;
			if (!_currentSpawnedPlayerAvatar.CustomAvatar.AllowHeightCalibration) return;

			float playerArmLength = PlayerPrefs.GetFloat(PlayerArmLengthKey, PlayerDefaultArmLength);
			float playerViewPointY = PlayerPrefs.GetFloat(PlayerViewPointYKey, PlayerDefaultViewPortY);

			_currentAvatarArmLength = _currentAvatarArmLength ?? AvatarMeasurement.MeasureArmLength(_currentSpawnedPlayerAvatar.GameObject);
			var avatarArmLength =  _currentAvatarArmLength ?? playerArmLength;
			Plugin.Log("Avatar Arm Length: " + avatarArmLength);

			var avatarViewPointY = _currentSpawnedPlayerAvatar.CustomAvatar.ViewPoint?.position.y ?? playerViewPointY;

			// fbx root gameobject
			var animator = _currentSpawnedPlayerAvatar.GameObject.GetComponentInChildren<Animator>();
			if (animator == null) { Plugin.Log("Animator not found"); return; }

			// scale
			var scale = playerArmLength / avatarArmLength;
			_currentSpawnedPlayerAvatar.GameObject.transform.localScale = _startAvatarLocalScale * scale;

			// translate root for floor level
			const float FloorLevelOffset = 0.04f; // a heuristic value from testing on oculus rift
			var offset = (playerViewPointY - (avatarViewPointY * scale)) + FloorLevelOffset;
			var avatarTranslate = Vector3.up * (offset - _currentAvatarOffsetY);
			_currentAvatarOffsetY = offset;

			animator.transform.Translate(avatarTranslate);

			// translate platform
			var platformTranslate = Vector3.up * (offset - _currentPlatformOffsetY);
			GameObject.Find("Platform Loader")?.transform.Translate(platformTranslate);
			_currentPlatformOffsetY = offset;

			Plugin.Log("Avatar fitted with scale: " + scale + " yoffset: " + offset);
		}

		private void FixAvatar()
		{
			var animator = _currentSpawnedPlayerAvatar.GameObject.GetComponentInChildren<Animator>();
			if (animator != null && animator.avatar && animator.isHuman)
			{
				// grip fix
				void fixGrip(HumanBodyBones littleFingerBoneName, HumanBodyBones indexFingerBoneName, HumanBodyBones wristBoneName, HumanBodyBones elbowBoneName,
					Transform handObject, Quaternion fixRotation, Vector3 fixTargetOffset, Vector3 baseArmDirection)
				{
					if (handObject == null) return;
					var handTarget = handObject.GetChild(0);
					if (handTarget == null) return;

					var littleFinger = animator.GetBoneTransform(littleFingerBoneName);
					var indexFinger = animator.GetBoneTransform(indexFingerBoneName);
					var wrist = animator.GetBoneTransform(wristBoneName);
					var elbow = animator.GetBoneTransform(elbowBoneName);

					var anatomicBaseRotation = Quaternion.LookRotation(indexFinger.position - littleFinger.position, wrist.position - elbow.position);
					var fingerThickness = (littleFinger.position - indexFinger.position).magnitude;
					fixTargetOffset.Scale(new Vector3(0.05f, fingerThickness, 0.05f));
					var anatomicBasePosition = indexFinger.position;
					var rotationToPose = Quaternion.FromToRotation(baseArmDirection, wrist.position - elbow.position);

					handTarget.parent = null;
					handObject.position = anatomicBasePosition + (rotationToPose * fixTargetOffset);
					handObject.localRotation = anatomicBaseRotation * fixRotation;
					handTarget.parent = handObject;

					handTarget.rotation = wrist.rotation;
					handTarget.position = wrist.position;

					Plugin.Log("Grip fix applied. " + anatomicBasePosition);
				}
				var targetOffset = new Vector3(-0.52f, -0.34f, 0.99f);
				var rotation = new Quaternion(0.005924877f, 0.294924f, 0.9530304f, -0.06868639f);
				fixGrip(HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm,
					_currentSpawnedPlayerAvatar.GameObject.transform.Find("LeftHand"),
					rotation, targetOffset, new Vector3(-1f, 0f, 0f));
				rotation.x *= -1f;
				rotation.w *= -1f;
				targetOffset.x *= -1f;
				fixGrip(HumanBodyBones.RightLittleProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm,
					_currentSpawnedPlayerAvatar.GameObject.transform.Find("RightHand"),
					rotation, targetOffset, new Vector3(1f, 0f, 0f));
			}
			// inject late fixer
			_currentSpawnedPlayerAvatar.GameObject.AddComponent<IKSolverFixer>();
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

		private class IKSolverFixer : MonoBehaviour
		{
			private void LateFix()
			{
				var vrik = GetComponentInChildren<AvatarScriptPack.VRIK>();
				if (vrik == null) return;
				// force plant feet feature disabled and you can jump
				vrik.solver.plantFeet = false;
				// other paternalistic assignings
				vrik.solver.spine.neckStiffness = 0f;
				vrik.solver.spine.headClampWeight = 0.4f;
				vrik.solver.spine.bodyPosStiffness = 0.3f;
				vrik.solver.spine.bodyRotStiffness = 0f;
				vrik.solver.spine.maintainPelvisPosition = 0f;
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

			public void Start()
			{
				// override values after the start of IKManagerAdvanced
				Invoke("LateFix", 0.1f);
			}
		}
	}
}
 