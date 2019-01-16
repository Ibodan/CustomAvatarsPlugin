using UnityEngine;
using System.Collections;
using System;
using AvatarScriptPack;

namespace AvatarScriptPack {

	/// <summary>
	/// Hybrid %IK solver designed for mapping a character to a VR headset and 2 hand controllers 
	/// </summary>
	public partial class IKSolverVR: IKSolver {
		
		/// <summary>
		/// 4-segmented analytic arm chain.
		/// </summary>
		[System.Serializable]
		public class Arm: BodyPart {

			[System.Serializable]
			public enum ShoulderRotationMode {
				YawPitch,
				FromTo
			}

			[Tooltip("The hand target")]
			/// <summary>
			/// The hand target.
			/// </summary>
			public Transform target;

			[Tooltip("The elbow will be bent towards this Transform if 'Bend Goal Weight' > 0.")]
			/// <summary>
			/// The elbow will be bent towards this Transform if 'Bend Goal Weight' > 0.
			/// </summary>
			public Transform bendGoal;

			[Tooltip("Positional weight of the hand target.")]
			/// <summary>
			/// Positional weight of the hand target.
			/// </summary>
			[Range(0f, 1f)] public float positionWeight = 1f;

			[Tooltip("Rotational weight of the hand target")]
			/// <summary>
			/// Rotational weight of the hand target.
			/// </summary>
			[Range(0f, 1f)] public float rotationWeight = 1f;

			[Tooltip("Different techniques for shoulder bone rotation.")]
			/// <summary>
			/// Different techniques for shoulder bone rotation.
			/// </summary>
			public ShoulderRotationMode shoulderRotationMode = ShoulderRotationMode.YawPitch;

			[Tooltip("The weight of shoulder rotation")]
			/// <summary>
			/// The weight of shoulder rotation
			/// </summary>
			[Range(0f, 1f)] public float shoulderRotationWeight = 1f;

			[Tooltip("If greater than 0, will bend the elbow towards the 'Bend Goal' Transform.")]
			/// <summary>
			/// If greater than 0, will bend the elbow towards the 'Bend Goal' Transform.
			/// </summary>
			[Range(0f, 1f)] public float bendGoalWeight;

			[Tooltip("Angular offset of the elbow bending direction.")]
			/// <summary>
			/// Angular offset of the elbow bending direction.
			/// </summary>
			[Range(-180f, 180f)] public float swivelOffset;

			[Tooltip("Local axis of the hand bone that points from the wrist towards the palm. Used for defining hand bone orientation.")]
			/// <summary>
			/// Local axis of the hand bone that points from the wrist towards the palm. Used for defining hand bone orientation.
			/// </summary>
			public Vector3 wristToPalmAxis = Vector3.zero;

			[Tooltip("Local axis of the hand bone that points from the palm towards the thumb. Used for defining hand bone orientation.")]
			/// <summary>
			/// Local axis of the hand bone that points from the palm towards the thumb. Used for defining hand bone orientation.
			/// </summary>
			public Vector3 palmToThumbAxis = Vector3.zero;

			[Tooltip("Use this to make the arm shorter/longer.")]
			/// <summary>
			/// Use this to make the arm shorter/longer.
			/// </summary>
			[Range(0.01f, 2f)]
			public float armLengthMlp = 1f;

			[Tooltip("Evaluates stretching of the arm by target distance relative to arm length. Value at time 1 represents stretching amount at the point where distance to the target is equal to arm length. Value at time 2 represents stretching amount at the point where distance to the target is double the arm length. Value represents the amount of stretching. Linear stretching would be achieved with a linear curve going up by 45 degrees. Increase the range of stretching by moving the last key up and right at the same amount. Smoothing in the curve can help reduce elbow snapping (start stretching the arm slightly before target distance reaches arm length).")]
			/// <summary>
			/// Evaluates stretching of the arm by target distance relative to arm length. Value at time 1 represents stretching amount at the point where distance to the target is equal to arm length. Value at time 2 represents stretching amount at the point where distance to the target is double the arm length. Value represents the amount of stretching. Linear stretching would be achieved with a linear curve going up by 45 degrees. Increase the range of stretching by moving the last key up and right at the same amount. Smoothing in the curve can help reduce elbow snapping (start stretching the arm slightly before target distance reaches arm length).
			/// </summary>
			public AnimationCurve stretchCurve;

			/// <summary>
			/// Target position of the hand. Will be overwritten if target is assigned.
			/// </summary>
			[NonSerialized][HideInInspector] public Vector3 IKPosition;

			/// <summary>
			/// Target rotation of the hand. Will be overwritten if target is assigned.
			/// </summary>
			[NonSerialized][HideInInspector] public Quaternion IKRotation = Quaternion.identity;

			/// <summary>
			/// The bending direction of the limb. Will be used if bendGoalWeight is greater than 0. Will be overwritten if bendGoal is assigned.
			/// </summary>
			[NonSerialized][HideInInspector] public Vector3 bendDirection = Vector3.back;

			/// <summary>
			/// Position offset of the hand. Will be applied on top of hand target position and reset to Vector3.zero after each update.
			/// </summary>
			[NonSerialized][HideInInspector] public Vector3 handPositionOffset;

			// Gets the target position of the hand.
			public Vector3 position { get; private set; }
			
			// Gets the target rotation of the hand
			public Quaternion rotation { get; private set; }

			private bool hasShoulder;
			private VirtualBone shoulder { get { return bones[0]; }}
			private VirtualBone upperArm { get { return bones[1]; }}
			private VirtualBone forearm { get { return bones[2]; }}
			private VirtualBone hand { get { return bones[3]; }}
			private Vector3 chestForwardAxis;
			private Vector3 chestUpAxis;
			private Quaternion chestRotation = Quaternion.identity;
			private Vector3 chestForward;
			private Vector3 chestUp;
			private Quaternion forearmRelToUpperArm = Quaternion.identity;

			private const float yawOffsetAngle = 45f;
			private const float pitchOffsetAngle = -30f;

			protected override void OnRead(Vector3[] positions, Quaternion[] rotations, bool hasChest, bool hasNeck, bool hasShoulders, bool hasToes, int rootIndex, int index) {
				Vector3 shoulderPosition = positions[index];
				Quaternion shoulderRotation = rotations[index];
				Vector3 upperArmPosition = positions[index + 1];
				Quaternion upperArmRotation = rotations[index + 1];
				Vector3 forearmPosition = positions[index + 2];
				Quaternion forearmRotation = rotations[index + 2];
				Vector3 handPosition = positions[index + 3];
				Quaternion handRotation = rotations[index + 3];

				if (!initiated) {
					IKPosition = handPosition;
					IKRotation = handRotation;
					rotation = IKRotation;

					this.hasShoulder = hasShoulders;

					bones = new VirtualBone[hasShoulder? 4: 3];

					if (hasShoulder) {
						bones[0] = new VirtualBone(shoulderPosition, shoulderRotation);
						bones[1] = new VirtualBone(upperArmPosition, upperArmRotation);
						bones[2] = new VirtualBone(forearmPosition, forearmRotation);
						bones[3] = new VirtualBone(handPosition, handRotation);
					} else {
						this.bones[0] = new VirtualBone(upperArmPosition, upperArmRotation);
						this.bones[1] = new VirtualBone(forearmPosition, forearmRotation);
						this.bones[2] = new VirtualBone(handPosition, handRotation);
					}

					chestForwardAxis = Quaternion.Inverse(rootRotation) * (rotations[0] * Vector3.forward);
					chestUpAxis = Quaternion.Inverse(rootRotation) * (rotations[0] * Vector3.up);
				} 

				if (hasShoulder) {
					bones[0].Read(shoulderPosition, shoulderRotation);
					bones[1].Read(upperArmPosition, upperArmRotation);
					bones[2].Read(forearmPosition, forearmRotation);
					bones[3].Read(handPosition, handRotation);
				} else {
					bones[0].Read(upperArmPosition, upperArmRotation);
					bones[1].Read(forearmPosition, forearmRotation);
					bones[2].Read(handPosition, handRotation);
				}
			}

			public override void PreSolve() {
				if (target != null) {
					IKPosition = target.position;
					IKRotation = target.rotation;
				}

				position = V3Tools.Lerp(hand.solverPosition, IKPosition, positionWeight);
				rotation = QuaTools.Lerp(hand.solverRotation, IKRotation, rotationWeight);

				shoulder.axis = shoulder.axis.normalized;
				forearmRelToUpperArm = Quaternion.Inverse(upperArm.solverRotation) * forearm.solverRotation;		
			}

			public override void ApplyOffsets() {
				position += handPositionOffset;
			}

			private void Stretching() {
				// Adjusting arm length
				float armLength = upperArm.length + forearm.length;
				Vector3 elbowAdd = Vector3.zero;
				Vector3 handAdd = Vector3.zero;

				if (armLengthMlp != 1f) {
					armLength *= armLengthMlp;
					elbowAdd = (forearm.solverPosition - upperArm.solverPosition) * (armLengthMlp - 1f);
					handAdd = (hand.solverPosition - forearm.solverPosition) * (armLengthMlp - 1f);
					forearm.solverPosition += elbowAdd;
					hand.solverPosition += elbowAdd + handAdd;
				}

				// Stretching
				if (stretchCurve != null) {
					float distanceToTarget = Vector3.Distance(upperArm.solverPosition, position);
					float stretchF = distanceToTarget / armLength;
					float m = stretchCurve.Evaluate(stretchF);
					m *= positionWeight;

					elbowAdd = (forearm.solverPosition - upperArm.solverPosition) * m;
					handAdd = (hand.solverPosition - forearm.solverPosition) * m;
				}

				forearm.solverPosition += elbowAdd;
				hand.solverPosition += elbowAdd + handAdd;
			}

			public void Solve(bool isLeft) {
				chestRotation = Quaternion.LookRotation(rootRotation * chestForwardAxis, rootRotation * chestUpAxis);
				chestForward = chestRotation * Vector3.forward;
				chestUp = chestRotation * Vector3.up;

				//Debug.DrawRay (Vector3.up * 2f, chestForward);
				//Debug.DrawRay (Vector3.up * 2f, chestUp);

				if (hasShoulder && shoulderRotationWeight > 0f) {
					switch(shoulderRotationMode) {
					case ShoulderRotationMode.YawPitch:
						Vector3 sDir = position - shoulder.solverPosition;
						sDir = sDir.normalized;

						// Shoulder Yaw
						float yOA = isLeft? yawOffsetAngle: -yawOffsetAngle;
						Quaternion yawOffset = Quaternion.AngleAxis((isLeft? -90f: 90f) + yOA, chestUp);
						Quaternion workingSpace = yawOffset * chestRotation;

						//Debug.DrawRay(Vector3.up * 2f, workingSpace * Vector3.forward);
						//Debug.DrawRay(Vector3.up * 2f, workingSpace * Vector3.up);

						Vector3 sDirWorking = Quaternion.Inverse(workingSpace) * sDir;

						//Debug.DrawRay(Vector3.up * 2f, sDirWorking);

						float yaw = Mathf.Atan2(sDirWorking.x, sDirWorking.z) * Mathf.Rad2Deg;

						float dotY = Vector3.Dot(sDirWorking, Vector3.up);
						dotY = 1f - Mathf.Abs(dotY);
						yaw *= dotY;

						yaw -= yOA;
						float yawLimitMin = isLeft? -20f: -50f;
						float yawLimitMax = isLeft? 50f: 20f;
						yaw = DamperValue(yaw, isLeft ? -60 : 0, isLeft ? 0 : 60, 0.7f); // back, forward

						Vector3 f = shoulder.solverRotation * shoulder.axis;
						Vector3 t = workingSpace * (Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward);
						Quaternion yawRotation = Quaternion.FromToRotation(f, t);

						//Debug.DrawRay(Vector3.up * 2f, f, Color.red);
						//Debug.DrawRay(Vector3.up * 2f, t, Color.green);

						//Debug.DrawRay(Vector3.up * 2f, yawRotation * Vector3.forward, Color.blue);
						//Debug.DrawRay(Vector3.up * 2f, yawRotation * Vector3.up, Color.green);
						//Debug.DrawRay(Vector3.up * 2f, yawRotation * Vector3.right, Color.red);

						// Shoulder Pitch
						Quaternion pitchOffset = Quaternion.AngleAxis(isLeft? -90f: 90f, chestUp);
						workingSpace = pitchOffset * chestRotation;
						workingSpace = Quaternion.AngleAxis(isLeft? pitchOffsetAngle: - pitchOffsetAngle, chestForward) * workingSpace;

						//Debug.DrawRay(Vector3.up * 2f, workingSpace * Vector3.forward);
						//Debug.DrawRay(Vector3.up * 2f, workingSpace * Vector3.up);

						sDir = position - (shoulder.solverPosition + chestRotation * (isLeft? Vector3.right: Vector3.left) * mag);
						sDirWorking = Quaternion.Inverse(workingSpace) * sDir;

						//Debug.DrawRay(Vector3.up * 2f, sDirWorking);

						float pitch = Mathf.Atan2(sDirWorking.y, sDirWorking.z) * Mathf.Rad2Deg;

						pitch -= pitchOffsetAngle;
						pitch = DamperValue(pitch, -45f - pitchOffsetAngle, 45f - pitchOffsetAngle);
						Quaternion pitchRotation = Quaternion.AngleAxis(-pitch, workingSpace * Vector3.right);

						//Debug.DrawRay(Vector3.up * 2f, pitchRotation * Vector3.forward, Color.green);
						//Debug.DrawRay(Vector3.up * 2f, pitchRotation * Vector3.up, Color.green);

						// Rotate bones
						Quaternion sR = pitchRotation * yawRotation;
						if (shoulderRotationWeight * positionWeight < 1f) sR = Quaternion.Lerp(Quaternion.identity, sR, shoulderRotationWeight * positionWeight);
						VirtualBone.RotateBy(bones, sR);

						Stretching();

						// Solve trigonometric
						VirtualBone.SolveTrigonometric(bones, 1, 2, 3, position, GetBendNormal(position - upperArm.solverPosition), positionWeight);

						float p = Mathf.Clamp(pitch * 2f * positionWeight, 0f, 180f);
						shoulder.solverRotation = Quaternion.AngleAxis(p, shoulder.solverRotation * (isLeft? shoulder.axis: -shoulder.axis)) * shoulder.solverRotation;
						upperArm.solverRotation = Quaternion.AngleAxis(p, upperArm.solverRotation * (isLeft? upperArm.axis: -upperArm.axis)) * upperArm.solverRotation;

						// Additional pass to reach with the shoulders
						//VirtualBone.SolveTrigonometric(bones, 0, 1, 3, position, Vector3.Cross(upperArm.solverPosition - shoulder.solverPosition, hand.solverPosition - shoulder.solverPosition), positionWeight * 0.5f);
					break;
					case ShoulderRotationMode.FromTo:
						Quaternion shoulderRotation = shoulder.solverRotation;

						Quaternion r = Quaternion.FromToRotation((upperArm.solverPosition - shoulder.solverPosition).normalized + chestForward, position - shoulder.solverPosition);
						r = Quaternion.Slerp(Quaternion.identity, r, 0.5f * shoulderRotationWeight * positionWeight);
						VirtualBone.RotateBy(bones, r);

						Stretching();

						VirtualBone.SolveTrigonometric(bones, 0, 2, 3, position, Vector3.Cross(forearm.solverPosition - shoulder.solverPosition, hand.solverPosition - shoulder.solverPosition), 0.5f * shoulderRotationWeight * positionWeight);
						VirtualBone.SolveTrigonometric(bones, 1, 2, 3, position, GetBendNormal(position - upperArm.solverPosition), positionWeight);

						// Twist shoulder and upper arm bones when holding hands up
						Quaternion q = Quaternion.Inverse(Quaternion.LookRotation(chestUp, chestForward));
						Vector3 vBefore = q * (shoulderRotation * shoulder.axis);
						Vector3 vAfter = q * (shoulder.solverRotation * shoulder.axis);
						float angleBefore = Mathf.Atan2(vBefore.x, vBefore.z) * Mathf.Rad2Deg;
						float angleAfter = Mathf.Atan2(vAfter.x, vAfter.z) * Mathf.Rad2Deg;
						float pitchAngle = Mathf.DeltaAngle(angleBefore, angleAfter);
						if (isLeft) pitchAngle = -pitchAngle;
						pitchAngle = Mathf.Clamp(pitchAngle * 2f * positionWeight, 0f, 180f);

						shoulder.solverRotation = Quaternion.AngleAxis(pitchAngle, shoulder.solverRotation * (isLeft? shoulder.axis: -shoulder.axis)) * shoulder.solverRotation;
						upperArm.solverRotation = Quaternion.AngleAxis(pitchAngle, upperArm.solverRotation * (isLeft? upperArm.axis: -upperArm.axis)) * upperArm.solverRotation;
					break;
					}
				} else {
					Stretching();

					// Solve arm trigonometric
					VirtualBone.SolveTrigonometric(bones, 1, 2, 3, position, GetBendNormal(position - upperArm.solverPosition), positionWeight);
				}

				// Fix forearm twist relative to upper arm
				Quaternion forearmFixed = upperArm.solverRotation * forearmRelToUpperArm;
				Quaternion fromTo = Quaternion.FromToRotation(forearmFixed * forearm.axis, hand.solverPosition - forearm.solverPosition);
				RotateTo(forearm, fromTo * forearmFixed, positionWeight);

				// Set hand rotation
				if (rotationWeight >= 1f) {
					hand.solverRotation = rotation;
				} else if (rotationWeight > 0f) {
					hand.solverRotation = Quaternion.Lerp (hand.solverRotation, rotation, rotationWeight);
				}
			}

			public override void ResetOffsets() {
				handPositionOffset = Vector3.zero;
			}

			public override void Write(ref Vector3[] solvedPositions, ref Quaternion[] solvedRotations) {
				if (hasShoulder) {
					solvedPositions[index] = shoulder.solverPosition;
					solvedRotations[index] = shoulder.solverRotation;
				}

				solvedPositions[index + 1] = upperArm.solverPosition;
				solvedPositions[index + 2] = forearm.solverPosition;
				solvedPositions[index + 3] = hand.solverPosition;

				solvedRotations[index + 1] = upperArm.solverRotation;
				solvedRotations[index + 2] = forearm.solverRotation;
				solvedRotations[index + 3] = hand.solverRotation;
			}

			private float DamperValue(float value, float min, float max, float weight = 1f) {
				float range = max - min;

				if (weight < 1f) {
					float mid = max - range * 0.5f;
					float v = value - mid;
					v *= 0.5f;
					value = mid + v;
				}

				value -= min;

				float t = Mathf.Clamp(value / range, 0f, 1f);
				float tEased = AvatarScriptPack.Interp.Float(t, InterpolationMode.InOutQuintic);
				return Mathf.Lerp(min, max, tEased);
			}

			private Vector3 GetBendNormal(Vector3 dir) {
				if (bendGoal != null) bendDirection = bendGoal.position - bones[1].solverPosition;

				Vector3 armDir = bones[0].solverRotation * bones[0].axis;

				Vector3 f = Vector3.down;
				Vector3 t = Quaternion.Inverse(chestRotation) * dir.normalized + Vector3.forward;
				Quaternion q = Quaternion.FromToRotation(f, t);

				Vector3 b = q * Vector3.back;

				f = Quaternion.Inverse(chestRotation) * armDir;
				t = Quaternion.Inverse(chestRotation) * dir;
				q = Quaternion.FromToRotation(f, t);
				b = q * b;

				b = chestRotation * b;

				b = Vector3.Slerp(b, armDir, 0.5f);
				Vector3 vectorHand = q * Vector3.Slerp(wristToPalmAxis, palmToThumbAxis, 0.85f) * -1.0f;
				float handWeight = (Vector3.Dot(b, vectorHand) + 1.0f) * 0.5f * 0.75f; // to range of 0 - 0.75
				b = Vector3.Slerp(b, vectorHand, handWeight);

				if (bendGoalWeight > 0f) {
					b = Vector3.Slerp(b, bendDirection, bendGoalWeight);
				}

				if (swivelOffset != 0f) b = Quaternion.AngleAxis(swivelOffset, -dir) * b;

				return Vector3.Cross(b, dir);
			}

			private void Visualize(VirtualBone bone1, VirtualBone bone2, VirtualBone bone3, Color color) {
				Debug.DrawLine(bone1.solverPosition, bone2.solverPosition, color);
				Debug.DrawLine(bone2.solverPosition, bone3.solverPosition, color);
			}
		}
	}
}

