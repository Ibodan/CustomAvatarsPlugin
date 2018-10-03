using System.Linq;
using CustomAvatar;
using UnityEngine;

namespace AvatarScriptPack
{
	public class FirstPersonExclusion : MonoBehaviour
	{
		public GameObject[] Exclude;

		private int[] _startLayers;

		private void OnEnable()
		{
			if (Exclude == null)
			{
				Destroy(this);
				return;
			}

			_startLayers = Exclude.Select(x => x.layer).ToArray();
		}

		private void OnDisable()
		{
		}

		public void OnFirstPersonEnabledChanged(bool firstPersonEnabled)
		{
			for (var i = 0; i < Exclude.Length; i++)
			{
				var excludeObject = Exclude[i];
				excludeObject.layer = firstPersonEnabled ? AvatarLayers.OnlyInThirdPerson : _startLayers[i];
			}
		}
	}
}