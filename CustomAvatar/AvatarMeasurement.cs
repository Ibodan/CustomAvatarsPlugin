﻿using System;
using UnityEngine;

namespace CustomAvatar
{
	public static class AvatarMeasurement
	{
		public const float DefaultPlayerHeight = 1.8f;
		private const float EyeToTopOfHeadDistance = 0.06f;
		private const float MinHeight = 1.4f;
		private const float MaxHeight = 2f;
		
		public static float MeasureHeight(GameObject avatarGameObject, Transform viewPoint)
		{
			var localPosition = avatarGameObject.transform.InverseTransformPoint(viewPoint.position);
			var height = localPosition.y + EyeToTopOfHeadDistance;
			
			//This is to handle cases where the head might be at 0,0,0, like in a non-IK avatar.
			if (height < MinHeight || height > MaxHeight)
			{
				height = DefaultPlayerHeight;
			}
			
			return Mathf.Clamp(height, MinHeight, MaxHeight);
		}

		public static float? MeasureArmLength(GameObject avatarGameObject)
		{
			var animator = avatarGameObject.GetComponentInChildren<Animator>();
			if (animator == null || animator.avatar == null || !animator.isHuman || !animator.enabled)
			{
				Plugin.Log("animator for human not found");
				return null;
			}

			var leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
			var rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position;
			var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
			return Vector3.Distance(leftHand, leftShoulder) * 2.0f + Vector3.Distance(leftShoulder, rightShoulder);
		}
	}
}