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
			ResizePlayerAvatar();
			InitialFixAvatar();
			GripFixPlayerAvatar();

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
		private const string PlayerGripAngleYKey = "AvatarAutoFitting.PlayerGripAngleY";
		private const string PlayerGripOffsetZKey = "AvatarAutoFitting.PlayerGripOffsetZ";
		private float PlayerDefaultViewPointY = BeatSaberUtil.GetPlayerHeight() - 0.11f;
		private float PlayerDefaultArmLength = BeatSaberUtil.GetPlayerHeight() * 0.88f;

		private Animator FindAvatarAnimator()
		{
			var vrik = _currentSpawnedPlayerAvatar.GameObject.GetComponentInChildren<AvatarScriptPack.VRIK>();
			if (vrik == null) return null;
			var animator = vrik.gameObject.GetComponentInChildren<Animator>();
			if (animator.avatar == null || !animator.isHuman) return null;
			return animator;
		}

		private void ResizePlayerAvatar()
		{
			if (_currentSpawnedPlayerAvatar?.GameObject == null) return;
			if (!_currentSpawnedPlayerAvatar.CustomAvatar.AllowHeightCalibration) return;

			// also used as fbx root object
			var animator = FindAvatarAnimator();
			if (animator == null) { Plugin.Log("Animator of human not found"); return; }

			float playerArmLength = PlayerPrefs.GetFloat(PlayerArmLengthKey, PlayerDefaultArmLength);
			float playerViewPointY = PlayerPrefs.GetFloat(PlayerViewPointYKey, PlayerDefaultViewPointY);

			_currentAvatarArmLength = _currentAvatarArmLength ?? AvatarMeasurement.MeasureArmLength(animator);
			var avatarArmLength = _currentAvatarArmLength ?? playerArmLength;
			Plugin.Log("Avatar Arm Length: " + avatarArmLength);

			var avatarViewPointY = _currentSpawnedPlayerAvatar.CustomAvatar.ViewPoint?.position.y ?? playerViewPointY;

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
		}

		private void GripFixPlayerAvatar()
		{
			var animator = FindAvatarAnimator();
			if (animator != null && animator.avatar && animator.isHuman)
			{
				void fixGrip(HumanBodyBones indexFingerBoneName, Transform handObject, float gripAngleY, float gripAngleZ, float gripOffsetZ)
				{
					if (handObject == null) return;
					var handTarget = handObject.GetChild(0);
					if (handTarget == null) return;

					var indexFinger = animator.GetBoneTransform(indexFingerBoneName);

					var basePoint = indexFinger.Find("_baseGripPoint");
					if (basePoint == null) return;

					handTarget.parent = null;
					handObject.rotation = Quaternion.AngleAxis(gripAngleY, basePoint.up) * basePoint.rotation;
					handObject.rotation = Quaternion.AngleAxis(gripAngleZ, handObject.forward) * handObject.rotation;
					handObject.position = basePoint.position + (handObject.forward * gripOffsetZ);
					handTarget.parent = handObject;

					Plugin.Log("Grip alignment applied. " + gripAngleY + "," + gripAngleZ + "," + gripOffsetZ);
				}

				var rotZ = PlayerPrefs.GetFloat(PlayerGripAngleKey, 80.0f);
				var rotY = PlayerPrefs.GetFloat(PlayerGripAngleYKey, 15.0f);
				var offsetZ = PlayerPrefs.GetFloat(PlayerGripOffsetZKey, 0.06f);

				fixGrip(HumanBodyBones.RightIndexProximal, _currentSpawnedPlayerAvatar.GameObject.transform.Find("RightHand"), rotY, rotZ, offsetZ);
				fixGrip(HumanBodyBones.LeftIndexProximal, _currentSpawnedPlayerAvatar.GameObject.transform.Find("LeftHand"), rotY, rotZ, offsetZ);
			}
		}

		private void InitialFixAvatar()
		{
			var animator = FindAvatarAnimator();
			if (animator != null)
			{
				Vector3 nearestPointOfLines(Vector3 mainPoint1, Vector3 mainPoint2, Vector3 subPoint1, Vector3 subPoint2)
				{
					Vector3 vMain = mainPoint2 - mainPoint1;
					Vector3 vSub = subPoint2 - subPoint1;
					Vector3 vMainNorm = vMain.normalized;
					Vector3 vSubNorm = vSub.normalized;
					float dot = Vector3.Dot(vMainNorm, vSubNorm);
					if (dot == 1.0f) return mainPoint1; // parallel lines - avoid deviding by zero
					Vector3 lineToLine = subPoint1 - mainPoint1;
					float delta = (Vector3.Dot(lineToLine, vMainNorm) - dot * Vector3.Dot(lineToLine, vSubNorm)) / (1.0f - dot * dot);
					return mainPoint1 + delta * vMainNorm;
				}

				void fixGrip(Transform handObject, HumanBodyBones wristBoneName, HumanBodyBones indexFingerBoneName, HumanBodyBones indexFinger2BoneName)
				{
					if (handObject == null) return;
					var handTarget = handObject.GetChild(0);
					if (handTarget == null) return;

					var wrist = animator.GetBoneTransform(wristBoneName);
					var indexFinger = animator.GetBoneTransform(indexFingerBoneName);
					var indexFinger2 = animator.GetBoneTransform(indexFinger2BoneName);

					var origParent = handObject.parent;
					handTarget.parent = origParent;
					handObject.parent = handTarget;
					handTarget.rotation = wrist.rotation;
					handTarget.position = wrist.position;
					handObject.parent = origParent;
					handTarget.parent = handObject;

					Transform baseGripPoint = indexFinger.Find("_baseGripPoint");
					if (baseGripPoint == null)
					{
						baseGripPoint = new GameObject("_baseGripPoint").transform;
						baseGripPoint.parent = indexFinger;
					}
					// nearest point of initial handObject vector for a line by index finger vector
					baseGripPoint.position = nearestPointOfLines(handObject.position, handObject.position + handObject.forward, indexFinger.position, indexFinger2.position);
					var handYAxis = Vector3.Cross(handObject.forward, handObject.position - wrist.position);
					handYAxis.z = 0f;
					baseGripPoint.rotation = Quaternion.LookRotation(Vector3.forward , handYAxis);
				}
				fixGrip(_currentSpawnedPlayerAvatar.GameObject.transform.Find("RightHand"), HumanBodyBones.RightHand, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate);
				fixGrip(_currentSpawnedPlayerAvatar.GameObject.transform.Find("LeftHand"), HumanBodyBones.LeftHand, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate);

				// inject late fixer
				_currentSpawnedPlayerAvatar.GameObject.AddComponent<IKSolverMender>();
			}
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
			var v = PlayerPrefs.GetFloat(PlayerGripAngleKey, 80.0f);
			v += 5.0f * step;
			PlayerPrefs.SetFloat(PlayerGripAngleKey, v);
			PlayerPrefs.Save();
			GripFixPlayerAvatar();
		}

		public void IncrementPlayerGripAngleY(int step)
		{
			var v = PlayerPrefs.GetFloat(PlayerGripAngleYKey, 15.0f);
			v += 5.0f * step;
			PlayerPrefs.SetFloat(PlayerGripAngleYKey, v);
			PlayerPrefs.Save();
			GripFixPlayerAvatar();
		}

		public void IncrementPlayerGripOffsetZ(int step)
		{
			var v = PlayerPrefs.GetFloat(PlayerGripOffsetZKey, 0.06f);
			v += 0.01f * step;
			PlayerPrefs.SetFloat(PlayerGripOffsetZKey, v);
			PlayerPrefs.Save();
			GripFixPlayerAvatar();
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
				if (vrik != null)
				{
					// force plant feet feature disabled and you can jump
					vrik.solver.plantFeet = false;
					var animator = vrik.gameObject.GetComponentInChildren<Animator>();
					if (animator != null && animator.avatar != null && animator.isHuman)
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
				Destroy(this);
			}

			private void Start()
			{
				// override values after the start of IKManagerAdvanced
				Invoke("LateFix", 0.1f);
			}
		}
	}
}
