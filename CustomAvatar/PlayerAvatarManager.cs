﻿using System;
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
			_currentAvatarOffsetY = 0f;
			_currentAvatarArmLength = null;
			_prevPlayerHeight = -1;
			ResizePlayerAvatar();
			OnFirstPersonEnabledChanged(Plugin.Instance.FirstPersonEnabled);
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
	}
}