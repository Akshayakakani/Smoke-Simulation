using UnityEngine;

namespace FluidSim
{
	[ExecuteAlways]
	public class SceneObject : MonoBehaviour
	{
		public SmokeComputeManager.ShapeType shapeType;
		public bool isObstacle;
		public Vector2 velocitySource;
		public Vector3 smokeRate;
		public float smokeRateMul = 1;
		public float targetTemperature;
		public bool isInstantSource;
		public Color gizmoCol;
		const float thickness = 0.06f;


		public SmokeComputeManager.SceneElement SceneElementData
		{
			get
			{
				var x = new SmokeComputeManager.SceneElement()
				{
					size = Size,
					pos = Centre,
					isObstacle = isObstacle ? 1 : 0,
					shapeType = (int)shapeType,
					smokeRate = smokeRate * smokeRateMul,
					targetTemperature = targetTemperature,
					velocitySource = velocitySource,
					isInstantSource = isInstantSource ? 1 : 0,
				};

				if (shapeType == SmokeComputeManager.ShapeType.Line)
				{
					Vector2 up = transform.up;
					x.posLineEnd = x.pos + up * transform.localScale.y;
				}

				return x;
			}
		}

		Vector2 Centre => transform.position;
		Vector2 Size => transform.localScale;

		void Update()
		{
			//return;

			Seb.Vis.Draw.StartLayerIfNotInMatching(Vector2.zero, 1, false);

			if (shapeType == SmokeComputeManager.ShapeType.Circle)
			{
				Seb.Vis.Draw.PointOutline(Centre, Size.x, thickness, gizmoCol);
			}
			else if (shapeType == SmokeComputeManager.ShapeType.Quad)
			{
				Seb.Vis.Draw.Quad(Centre, Size, gizmoCol);
				//Gizmos.DrawWireCube(Centre, Size);
			}
			else if (shapeType == SmokeComputeManager.ShapeType.Line)
			{
				SmokeComputeManager.SceneElement line = SceneElementData;
				Seb.Vis.Draw.Line(line.pos, line.posLineEnd, line.size.x, gizmoCol);
			}
		}


#if UNITY_EDITOR
		void OnDrawGizmos()
		{
			bool isSelected = false;
			foreach (GameObject selected in UnityEditor.Selection.gameObjects)
			{
				if (selected == gameObject) isSelected = true;
			}


			Color col = gizmoCol;
			col.a = Application.isPlaying ? 0.7f : 0.9f;

			if (isSelected)
			{
				Color.RGBToHSV(gizmoCol, out float h, out float s, out float v);
				col = Color.HSVToRGB(h, s - 0.1f, v + 0.35f);
				col.a = 1;
			}

			Gizmos.color = col;

			if (shapeType == SmokeComputeManager.ShapeType.Circle)
			{
				Gizmos.DrawWireSphere(Centre, Size.x);
			}
			else if (shapeType == SmokeComputeManager.ShapeType.Quad)
			{
				Gizmos.DrawWireCube(Centre, Size);
			}
		}
#endif
	}
}