﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IllusionPlugin;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomAvatar
{
	public class Plugin : IPlugin
	{
		private const string CustomAvatarsPath = "CustomAvatars";
		private const string FirstPersonEnabledKey = "avatarFirstPerson";
		private const string PreviousAvatarKey = "previousAvatar";
		
		private bool _init;
		private bool _firstPersonEnabled;
		private GameScenesManager _gameScenesManager;

		public Plugin()
		{
			Instance = this;
		}

		public event Action<bool> FirstPersonEnabledChanged;

		public static Plugin Instance { get; private set; }
		public AvatarLoader AvatarLoader { get; private set; }
		public PlayerAvatarManager PlayerAvatarManager { get; private set; }

		public bool FirstPersonEnabled
		{
			get { return _firstPersonEnabled; }
			private set
			{
				if (_firstPersonEnabled == value) return;

				_firstPersonEnabled = value;

				if (value)
				{
					PlayerPrefs.SetInt(FirstPersonEnabledKey, 0);
				}
				else
				{
					PlayerPrefs.DeleteKey(FirstPersonEnabledKey);
				}

				if (FirstPersonEnabledChanged != null)
				{
					FirstPersonEnabledChanged(value);
				}
			}
		}

		public string Name
		{
			get { return "Custom Avatars Plugin"; }
		}

		public string Version
		{
			get { return "3.1.3-beta"; }
		}

		public static void Log(string message)
		{
			Console.WriteLine("[CustomAvatarsPlugin] " + message);
			File.AppendAllText("CustomAvatarsPlugin-log.txt", "[Custom Avatars Plugin] " + message + Environment.NewLine);
		}

		public void OnApplicationStart()
		{
			if (_init) return;
			_init = true;
			
			File.WriteAllText("CustomAvatarsPlugin-log.txt", string.Empty);
			
			AvatarLoader = new AvatarLoader(CustomAvatarsPath, AvatarsLoaded);
			
			FirstPersonEnabled = PlayerPrefs.HasKey(FirstPersonEnabledKey);
			SceneManager.sceneLoaded += SceneManagerOnSceneLoaded;
		}

		public void OnApplicationQuit()
		{
			SceneManager.sceneLoaded -= SceneManagerOnSceneLoaded;

			if (PlayerAvatarManager != null)
				PlayerAvatarManager.AvatarChanged -= PlayerAvatarManagerOnAvatarChanged;
			if (_gameScenesManager != null)
				_gameScenesManager.transitionDidFinishEvent -= SetCameraCullingMask;
		}

		private void AvatarsLoaded(IReadOnlyList<CustomAvatar> loadedAvatars)
		{
			if (loadedAvatars.Count == 0)
			{
				Log("No custom avatars found in path " + Path.GetFullPath(CustomAvatarsPath));
				return;
			}

			var previousAvatarPath = PlayerPrefs.GetString(PreviousAvatarKey, null);
			var previousAvatar = AvatarLoader.Avatars.FirstOrDefault(x => x.FullPath == previousAvatarPath);
			
			PlayerAvatarManager = new PlayerAvatarManager(AvatarLoader, previousAvatar);
			PlayerAvatarManager.AvatarChanged += PlayerAvatarManagerOnAvatarChanged;
		}

		private void SceneManagerOnSceneLoaded(Scene newScene, LoadSceneMode mode)
		{
			if (_gameScenesManager == null)
			{
				_gameScenesManager = Resources.FindObjectsOfTypeAll<GameScenesManager>().FirstOrDefault();
				if (_gameScenesManager != null)
					_gameScenesManager.transitionDidFinishEvent += SetCameraCullingMask;
			}
		}

		private void PlayerAvatarManagerOnAvatarChanged(CustomAvatar newAvatar)
		{
			PlayerPrefs.SetString(PreviousAvatarKey, newAvatar.FullPath);
		}

		public void OnUpdate()
		{
			if (Input.GetKeyDown(KeyCode.PageUp))
			{
				if (PlayerAvatarManager == null) return;
				PlayerAvatarManager.SwitchToNextAvatar();
			}
			else if (Input.GetKeyDown(KeyCode.PageDown))
			{
				if (PlayerAvatarManager == null) return;
				PlayerAvatarManager.SwitchToPreviousAvatar();
			}
			else if (Input.GetKeyDown(KeyCode.Home))
			{
				FirstPersonEnabled = !FirstPersonEnabled;
			}
			else if (Input.GetKeyDown(KeyCode.End))
			{
				PlayerAvatarManager.MeasurePlayerViewPoint();
			}
			else if (Input.GetKeyDown(KeyCode.Period))
			{
				PlayerAvatarManager.IncrementPlayerArmLength(1);
			}
			else if (Input.GetKeyDown(KeyCode.Comma))
			{
				PlayerAvatarManager.IncrementPlayerArmLength(-1);
			}
			else if (Input.GetKeyDown(KeyCode.M))
			{
				PlayerAvatarManager.IncrementPlayerGripAngle(1);
			}
			else if (Input.GetKeyDown(KeyCode.N))
			{
				PlayerAvatarManager.IncrementPlayerGripAngle(-1);
			}
			else if (Input.GetKeyDown(KeyCode.J))
			{
				PlayerAvatarManager.IncrementPlayerGripAngleY(1);
			}
			else if (Input.GetKeyDown(KeyCode.H))
			{
				PlayerAvatarManager.IncrementPlayerGripAngleY(-1);
			}
			else if (Input.GetKeyDown(KeyCode.L))
			{
				PlayerAvatarManager.IncrementPlayerGripOffsetZ(1);
			}
			else if (Input.GetKeyDown(KeyCode.K))
			{
				PlayerAvatarManager.IncrementPlayerGripOffsetZ(-1);
			}
		}

		private void SetCameraCullingMask()
		{
			var mainCamera = Camera.main;
			if (mainCamera == null) return;
			mainCamera.cullingMask &= ~(1 << AvatarLayers.OnlyInThirdPerson);
			mainCamera.cullingMask |= 1 << AvatarLayers.OnlyInFirstPerson;
		}

		public void OnFixedUpdate()
		{
		}

		public void OnLevelWasInitialized(int level)
		{
		}

		public void OnLevelWasLoaded(int level)
		{
		}
	}
}