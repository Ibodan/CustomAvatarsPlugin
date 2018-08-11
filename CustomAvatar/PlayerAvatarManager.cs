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
			SceneManager.activeSceneChanged += SceneManagerOnActiveSceneChanged;
		}

		~PlayerAvatarManager()
		{
			Plugin.Instance.FirstPersonEnabledChanged -= OnFirstPersonEnabledChanged;
			SceneManager.activeSceneChanged -= SceneManagerOnActiveSceneChanged;
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
			_prevPlayerHeight = -1;
			ResizePlayerAvatar();
		}

		private void OnFirstPersonEnabledChanged(bool firstPersonEnabled)
		{
			if (_currentSpawnedPlayerAvatar == null) return;
			AvatarLayers.SetChildrenToLayer(_currentSpawnedPlayerAvatar.GameObject,
				firstPersonEnabled ? 0 : AvatarLayers.OnlyInThirdPerson);
		}

		private void SceneManagerOnActiveSceneChanged(Scene oldScene, Scene newScene)
		{
			ResizePlayerAvatar();
		}

		protected static string PlayerHandToHandKey = "AvatarAutoFitting.PlayerHandToHand";
		protected static string PlayerViewPortYKey = "AvatarAutoFitting.PlayerViewPortY";

		private void ResizePlayerAvatar()
		{
			if (_currentSpawnedPlayerAvatar?.GameObject == null) return;
			if (!_currentSpawnedPlayerAvatar.CustomAvatar.AllowHeightCalibration) return;

			float PlayerHandToHand = PlayerPrefs.GetFloat(PlayerHandToHandKey, 1.5f);
			float PlayerViewPointY = PlayerPrefs.GetFloat(PlayerViewPortYKey, 1.5f);

			var avatarHandToHand = AvatarMeasurement.MeasureHandToHand(_currentSpawnedPlayerAvatar.GameObject) ?? PlayerHandToHand;
			Plugin.Log("HandToHand: " + avatarHandToHand);

			var avatarViewPointY = _currentSpawnedPlayerAvatar.CustomAvatar.ViewPoint?.position.y ?? PlayerViewPointY;

			// fbx root gameobject
			var animator = _currentSpawnedPlayerAvatar.GameObject.GetComponentInChildren<Animator>();
			if (animator == null) { Plugin.Log("Animator not found"); return; }

			// scale
			var scale = PlayerHandToHand / avatarHandToHand;
			_currentSpawnedPlayerAvatar.GameObject.transform.localScale = _startAvatarLocalScale * scale;

			// translate root for floor level
			const float FloorLevelOffset = 0.04f; // a heuristic value from testing on oculus rift
			var offset = (PlayerViewPointY - (avatarViewPointY * scale)) + FloorLevelOffset;
			animator.transform.Translate(Vector3.up * offset);

			Plugin.Log("Avatar fitted with scale: " + scale + " yoffset: " + offset);
		}

		public void MeasurePlayerSize()
		{
			var active = SceneManager.GetActiveScene().GetRootGameObjects()[0].GetComponent<PlayerSizeScanner>();
			if (active != null)
			{
				GameObject.Destroy(active);
			}
			SceneManager.GetActiveScene().GetRootGameObjects()[0].AddComponent<PlayerSizeScanner>();
		}

		private class PlayerSizeScanner : MonoBehaviour
		{
			private PlayerAvatarInput playerInput = new PlayerAvatarInput();
			private const float initialValue = 0.5f;
			private float maxViewPointY = initialValue;
			private float maxHandToHandLength = initialValue;
			private int scanCount = 0;

			void Scan()
			{
				maxViewPointY = Math.Max(maxViewPointY, playerInput.HeadPosRot.Position.y);
				maxHandToHandLength = Math.Max(maxHandToHandLength, Vector3.Distance(playerInput.LeftPosRot.Position, playerInput.RightPosRot.Position));
				if (scanCount++ > 15)
				{
					Plugin.Log("Scanning finished. viewporty: " + maxViewPointY + " handtohand: " + maxHandToHandLength);
					if (maxViewPointY > initialValue && maxHandToHandLength > initialValue)
					{
						PlayerPrefs.SetFloat(PlayerViewPortYKey, maxViewPointY);
						PlayerPrefs.SetFloat(PlayerHandToHandKey, maxHandToHandLength);
						PlayerPrefs.Save();
					}
					Destroy(this);
				}
			}
			void Start()
			{
				Plugin.Log("PlayerSizeScanner starts scannig");
				InvokeRepeating("Scan", 1.0f, 0.2f);
			}
			void OnDestroy()
			{
				CancelInvoke();
			}
		}
	}
}