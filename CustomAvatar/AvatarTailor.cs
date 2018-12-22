using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomAvatar
{
	public class AvatarTailor
	{
		private float? _currentAvatarArmLength = null;
		private Vector3? _initialPlatformPosition = null;
		private float? _initialAvatarPositionY = null;
		private Vector3 _initialAvatarLocalScale = Vector3.one;

		private const string _kPlayerArmLengthKey = "CustomAvatar.Tailoring.PlayerArmLength";
		private const string _kResizePolicyKey = "CustomAvatar.Tailoring.ResizePolicy";
		private const string _kFloorMovePolicyKey = "CustomAvatar.Tailoring.FloorMovePolicy";
		private const string _kPlayerGripAngleKey = "AvatarAutoFitting.PlayerGripAngle";
		private const string _kPlayerGripAngleYKey = "AvatarAutoFitting.PlayerGripAngleY";
		private const string _kPlayerGripOffsetZKey = "AvatarAutoFitting.PlayerGripOffsetZ";

		public enum ResizePolicyType
		{
			AlignArmLength,
			AlignHeight,
			NeverResize
		}

		public enum FloorMovePolicyType
		{
			AllowMove,
			NeverMove
		}

		public float PlayerArmLength
		{
			get => PlayerPrefs.GetFloat(_kPlayerArmLengthKey, BeatSaberUtil.GetPlayerHeight() * 0.88f);
			private set => PlayerPrefs.SetFloat(_kPlayerArmLengthKey, value);
		}

		public ResizePolicyType ResizePolicy
		{
			get => (ResizePolicyType)PlayerPrefs.GetInt(_kResizePolicyKey, 1);
			set => PlayerPrefs.SetInt(_kResizePolicyKey, (int)value);
		}

		public FloorMovePolicyType FloorMovePolicy
		{
			get => (FloorMovePolicyType)PlayerPrefs.GetInt(_kFloorMovePolicyKey, 1);
			set => PlayerPrefs.SetInt(_kFloorMovePolicyKey, (int)value);
		}

		private Animator FindAvatarAnimator(GameObject gameObject)
		{
			var vrik = gameObject.GetComponentInChildren<AvatarScriptPack.VRIK>();
			if (vrik == null) return null;
			var animator = vrik.gameObject.GetComponentInChildren<Animator>();
			if (animator.avatar == null || !animator.isHuman) return null;
			return animator;
		}

		public void OnAvatarLoaded(SpawnedAvatar avatar)
		{
			_initialAvatarLocalScale = avatar.GameObject.transform.localScale;
			_initialAvatarPositionY = null;
			_currentAvatarArmLength = null;
		}

		public void ResizeAvatar(SpawnedAvatar avatar)
		{
			var animator = FindAvatarAnimator(avatar.GameObject);
			if (animator == null)
			{
				Plugin.Log("Tailor: Animator not found");
				return;
			}

			// compute scale
			float scale = 1.0f;
			if (ResizePolicy == ResizePolicyType.AlignArmLength)
			{
				float playerArmLength = PlayerArmLength;
				_currentAvatarArmLength = _currentAvatarArmLength ?? AvatarMeasurement.MeasureArmLength(animator);
				var avatarArmLength = _currentAvatarArmLength ?? playerArmLength;
				Plugin.Log("Avatar arm length: " + avatarArmLength);

				scale = playerArmLength / avatarArmLength;
			}
			else if (ResizePolicy == ResizePolicyType.AlignHeight)
			{
				scale = BeatSaberUtil.GetPlayerHeight() / avatar.CustomAvatar.Height;
			}

			// apply scale
			avatar.GameObject.transform.localScale = _initialAvatarLocalScale * scale;

			// compute offset
			float floorOffset = 0f;
			// give up moving original foot floors
			var originalFloor = GameObject.Find("MenuPlayersPlace") ?? GameObject.Find("Static/PlayersPlace");
			if (originalFloor != null && originalFloor.activeSelf == true) floorOffset = 0f;

			if (FloorMovePolicy == FloorMovePolicyType.AllowMove)
			{
				float playerViewPointHeight = BeatSaberUtil.GetPlayerViewPointHeight();
				float avatarViewPointHeight = avatar.CustomAvatar.ViewPoint?.position.y ?? playerViewPointHeight;
				_initialAvatarPositionY = _initialAvatarPositionY ?? animator.transform.position.y;
				const float FloorLevelOffset = 0.04f; // a heuristic value from testing on oculus rift
				floorOffset = playerViewPointHeight - (avatarViewPointHeight * scale) + FloorLevelOffset;
			}

			// apply offset
			animator.transform.position = new Vector3(animator.transform.position.x, floorOffset + _initialAvatarPositionY ?? 0, animator.transform.position.z);

			var customFloor = GameObject.Find("Platform Loader");
			if (customFloor != null)
			{
				_initialPlatformPosition = _initialPlatformPosition ?? customFloor.transform.position;
				var floorTailor = customFloor.AddComponent<FloorLevelTailor>();
				floorTailor.destination = (Vector3.up * floorOffset) + _initialPlatformPosition ?? Vector3.zero;
			}

			Plugin.Log("Avatar resized with scale: " + scale + " floor-offset: " + floorOffset);
		}

		public void GripFittingPlayerAvatar(GameObject avatarGameObject)
		{
			var animator = FindAvatarAnimator(avatarGameObject);
			if (animator == null) return;

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

			var rotZ = PlayerPrefs.GetFloat(_kPlayerGripAngleKey, 80.0f);
			var rotY = PlayerPrefs.GetFloat(_kPlayerGripAngleYKey, 15.0f);
			var offsetZ = PlayerPrefs.GetFloat(_kPlayerGripOffsetZKey, 0.06f);

			fixGrip(HumanBodyBones.RightIndexProximal, avatarGameObject.transform.Find("RightHand"), rotY, rotZ, offsetZ);
			fixGrip(HumanBodyBones.LeftIndexProximal, avatarGameObject.transform.Find("LeftHand"), rotY, 180 - rotZ, offsetZ);
		}

		public void PrepareGripFitting(GameObject avatarGameObject)
		{
			var animator = FindAvatarAnimator(avatarGameObject);
			if (animator == null) return;

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
				baseGripPoint.rotation = Quaternion.LookRotation(Vector3.forward, handYAxis);
			}

			fixGrip(avatarGameObject.transform.Find("RightHand"), HumanBodyBones.RightHand, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate);
			fixGrip(avatarGameObject.transform.Find("LeftHand"), HumanBodyBones.LeftHand, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate);
		}

		private class FloorLevelTailor : MonoBehaviour
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

		public void MeasurePlayerArmLength(Action<float> onProgress, Action<float> onFinished)
		{
			var active = SceneManager.GetActiveScene().GetRootGameObjects()[0].GetComponent<PlayerArmLengthMeasurement>();
			if (active != null)
			{
				GameObject.Destroy(active);
			}
			active = SceneManager.GetActiveScene().GetRootGameObjects()[0].AddComponent<PlayerArmLengthMeasurement>();
			active.onProgress = onProgress;
			active.onFinished = (result) =>
			{
				PlayerArmLength = result;
				onFinished(result);
			};
		}

		private class PlayerArmLengthMeasurement : MonoBehaviour
		{
			private PlayerAvatarInput playerInput = new PlayerAvatarInput();
			private const float initialValue = 0.5f;
			private float maxHandToHandLength = initialValue;
			private float updateTime = 0;
			public Action<float> onFinished = null;
			public Action<float> onProgress = null;

			void Scan()
			{
				var handToHandLength = Vector3.Distance(playerInput.LeftPosRot.Position, playerInput.RightPosRot.Position);
				if (maxHandToHandLength < handToHandLength)
				{
					maxHandToHandLength = handToHandLength;
					updateTime = Time.timeSinceLevelLoad;
				}
				else if (Time.timeSinceLevelLoad - updateTime > 2.0f)
				{
					onFinished?.Invoke(maxHandToHandLength);
					Destroy(this);
					return;
				}
				onProgress?.Invoke(maxHandToHandLength);
			}

			void Start()
			{
				InvokeRepeating("Scan", 1.0f, 0.2f);
			}

			void OnDestroy()
			{
				CancelInvoke();
			}
		}

		public void IncrementPlayerGripAngle(int step, GameObject avatarGameObject)
		{
			var v = PlayerPrefs.GetFloat(_kPlayerGripAngleKey, 80.0f);
			v += 5.0f * step;
			PlayerPrefs.SetFloat(_kPlayerGripAngleKey, v);
			PlayerPrefs.Save();
			GripFittingPlayerAvatar(avatarGameObject);
		}

		public void IncrementPlayerGripAngleY(int step, GameObject avatarGameObject)
		{
			var v = PlayerPrefs.GetFloat(_kPlayerGripAngleYKey, 15.0f);
			v += 5.0f * step;
			PlayerPrefs.SetFloat(_kPlayerGripAngleYKey, v);
			PlayerPrefs.Save();
			GripFittingPlayerAvatar(avatarGameObject);
		}

		public void IncrementPlayerGripOffsetZ(int step, GameObject avatarGameObject)
		{
			var v = PlayerPrefs.GetFloat(_kPlayerGripOffsetZKey, 0.06f);
			v += 0.01f * step;
			PlayerPrefs.SetFloat(_kPlayerGripOffsetZKey, v);
			PlayerPrefs.Save();
			GripFittingPlayerAvatar(avatarGameObject);
		}

	}
}
