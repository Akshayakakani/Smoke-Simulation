using Seb.Helpers;
using UnityEngine;
using static UnityEngine.Mathf;

namespace FluidSimCPU
{
	public class FluidGrid
	{
		public readonly int CellCountX;
		public readonly int CellCountY;
		public float CellSize;

		public readonly float[,] VelocitiesX;
		public readonly float[,] VelocitiesY;

		public readonly float[,] VelocitiesX_Temp;
		public readonly float[,] VelocitiesY_Temp;

		public readonly bool[,] SolidCellMap;
		readonly PressureSolveData[,] PressureSolveDataMap;

		public readonly float[,] PressureMap;
		public readonly float[,] SmokeMap;
		public readonly float[,] SmokeMapTemp;

		float TimeStep => 1 / 60f * TimeStepMul;
		const float Density = 1;
		public float TimeStepMul = 1;
		public float SOR = 1;

		readonly Vector2 boundsSize;
		readonly Vector2 bottomLeft;
		readonly float halfCellSize;

		public FluidGrid(int cellCountX, int cellCountY, float cellSize)
		{
			CellSize = cellSize;
			CellCountX = cellCountX;
			CellCountY = cellCountY;

			VelocitiesX = new float[cellCountX + 1, cellCountY];
			VelocitiesX_Temp = new float[cellCountX + 1, cellCountY];

			VelocitiesY = new float[cellCountX, cellCountY + 1];
			VelocitiesY_Temp = new float[cellCountX, cellCountY + 1];

			PressureMap = new float[cellCountX, cellCountY];
			SmokeMap = new float[cellCountX, cellCountY];
			SmokeMapTemp = new float[cellCountX, cellCountY];


			SolidCellMap = new bool[cellCountX, cellCountY];
			PressureSolveDataMap = new PressureSolveData[cellCountX, cellCountY];

			// ---- Initialize border cells as solid ----
			for (int x = 0; x < cellCountX; x++)
			{
				SolidCellMap[x, 0] = true;
				SolidCellMap[x, cellCountY - 1] = true;
			}

			for (int y = 0; y < cellCountY; y++)
			{
				SolidCellMap[0, y] = true;
				SolidCellMap[cellCountX - 1, y] = true;
			}

			boundsSize = new Vector2(CellCountX, CellCountY) * CellSize;
			bottomLeft = -boundsSize / 2;
			halfCellSize = CellSize / 2;
		}

		public Vector2 CellCentre(int x, int y) => bottomLeft + new Vector2(x + 0.5f, y + 0.5f) * CellSize;
		Vector2 LeftEdgeCentre(int x, int y) => CellCentre(x, y) - new Vector2(halfCellSize, 0);
		Vector2 BottomEdgeCentre(int x, int y) => CellCentre(x, y) - new Vector2(0, halfCellSize);

		public void RunPressureSolver(int numIts)
		{
			PreparePressureSolver();

			for (int i = 0; i < numIts; i++)
			{
				PressureSolve();
			}
		}

		void PressureSolve()
		{
			for (int x = 0; x < CellCountX; x++)
			{
				for (int y = 0; y < CellCountY; y++)
				{
					float newPressure;

					PressureSolveData info = PressureSolveDataMap[x, y];


					if (info.isSolid || info.flowEdgeCount == 0) newPressure = 0;
					else
					{
						float pressureTop = PressureMap[x, Min(y + 1, CellCountY - 1)] * info.flowTop;
						float pressureLeft = PressureMap[Max(x - 1, 0), y] * info.flowLeft;
						float pressureRight = PressureMap[Min(x + 1, CellCountX - 1), y] * info.flowRight;
						float pressureBottom = PressureMap[x, Max(y - 1, 0)] * info.flowBottom;

						float pressureSum = pressureRight + pressureLeft + pressureTop + pressureBottom;
						newPressure = (pressureSum - Density * CellSize * info.velocityTerm) / info.flowEdgeCount;
					}

					// SOR update
					float oldPressure = PressureMap[x, y];
					PressureMap[x, y] = oldPressure + (newPressure - oldPressure) * SOR;
				}
			}
		}


		public void UpdateVelocities()
		{
			float K = TimeStep / (Density * CellSize);

			// ---- Horizontal ----
			for (int x = 0; x < VelocitiesX.GetLength(0); x++)
			{
				for (int y = 0; y < VelocitiesX.GetLength(1); y++)
				{
					if (IsSolid(x, y) || IsSolid(x - 1, y))
					{
						continue;
					}

					float pressureRight = GetPressure(x, y);
					float pressureLeft = GetPressure(x - 1, y);
					VelocitiesX[x, y] -= K * (pressureRight - pressureLeft);
				}
			}

			// ---- Vertical ----

			for (int x = 0; x < VelocitiesY.GetLength(0); x++)
			{
				for (int y = 0; y < VelocitiesY.GetLength(1); y++)
				{
					if (IsSolid(x, y) || IsSolid(x, y - 1))
					{
						continue;
					}

					float pressureTop = GetPressure(x, y);
					float pressureBottom = GetPressure(x, y - 1);
					VelocitiesY[x, y] -= K * (pressureTop - pressureBottom);
				}
			}
		}

		public void AdvectVelocity()
		{
			// Horizontal
			for (int x = 0; x < VelocitiesX.GetLength(0); x++)
			{
				for (int y = 0; y < VelocitiesX.GetLength(1); y++)
				{
					if (IsSolid(x - 1, y) || IsSolid(x, y))
					{
						VelocitiesX_Temp[x, y] = VelocitiesX[x, y];
						continue;
					}

					Vector2 pos = LeftEdgeCentre(x, y);
					Vector2 vel = GetVelocityAtWorldPos(pos);
					Vector2 posPrev = pos - vel * TimeStep;
					VelocitiesX_Temp[x, y] = GetVelocityAtWorldPos(posPrev).x;
				}
			}

			// Vertical
			for (int x = 0; x < VelocitiesY.GetLength(0); x++)
			{
				for (int y = 0; y < VelocitiesY.GetLength(1); y++)
				{
					if (IsSolid(x, y - 1) || IsSolid(x, y))
					{
						VelocitiesY_Temp[x, y] = VelocitiesY[x, y];
						continue;
					}

					Vector2 pos = BottomEdgeCentre(x, y);
					Vector2 vel = GetVelocityAtWorldPos(pos);
					Vector2 posPrev = pos - vel * TimeStep;
					VelocitiesY_Temp[x, y] = GetVelocityAtWorldPos(posPrev).y;
				}
			}

			UpdateVelocitiesFromTemporary();
		}

		public void AdvectDye()
		{
			for (int x = 0; x < SmokeMap.GetLength(0); x++)
			{
				for (int y = 0; y < SmokeMap.GetLength(1); y++)
				{
					if (IsSolid(x - 1, y) || IsSolid(x, y)) continue;
					Vector2 pos = CellCentre(x, y);
					Vector2 vel = GetVelocityAtWorldPos(pos);
					Vector2 posPrev = pos - vel * TimeStep;


					float tx = (posPrev.x - bottomLeft.x) / (boundsSize.x);
					float ty = (posPrev.y - bottomLeft.y) / (boundsSize.y);
					float amount = Maths.SampleBilinear(SmokeMap, tx, ty, false);

					SmokeMapTemp[x, y] = amount;
				}
			}

			for (int x = 0; x < SmokeMap.GetLength(0); x++)
			{
				for (int y = 0; y < SmokeMap.GetLength(1); y++)
				{
					SmokeMap[x, y] = SmokeMapTemp[x, y];
				}
			}
		}


		void UpdateVelocitiesFromTemporary()
		{
			for (int x = 0; x < VelocitiesX.GetLength(0); x++)
			{
				for (int y = 0; y < VelocitiesX.GetLength(1); y++)
				{
					VelocitiesX[x, y] = VelocitiesX_Temp[x, y];
				}
			}

			for (int x = 0; x < VelocitiesY.GetLength(0); x++)
			{
				for (int y = 0; y < VelocitiesY.GetLength(1); y++)
				{
					VelocitiesY[x, y] = VelocitiesY_Temp[x, y];
				}
			}
		}

		public static float SampleBilinear(float[,] edgeValues, float cellSize, Vector2 worldPos)
		{
			int edgeCountX = edgeValues.GetLength(0);
			int edgeCountY = edgeValues.GetLength(1);
			float width = (edgeCountX - 1) * cellSize;
			float height = (edgeCountY - 1) * cellSize;

			// Calculate indices of each edge for the current cell
			float px = (worldPos.x + width / 2) / cellSize; // [0, countX]
			float py = (worldPos.y + height / 2) / cellSize; // [0, countY]

			int left = Clamp((int)px, 0, edgeCountX - 2);
			int bottom = Clamp((int)py, 0, edgeCountY - 2);
			int right = left + 1;
			int top = bottom + 1;

			// Calculate how far [0, 1] the input point is along the current cell
			float xFrac = Clamp01(px - left);
			float yFrac = Clamp01(py - bottom);

			// Bilinear interpolation
			float valueTop = Lerp(edgeValues[left, top], edgeValues[right, top], xFrac);
			float valueBottom = Lerp(edgeValues[left, bottom], edgeValues[right, bottom], xFrac);
			return Lerp(valueBottom, valueTop, yFrac);
		}

		public Vector2 GetVelocityAtWorldPos(Vector2 worldPos)
		{
			float velX = SampleBilinear(VelocitiesX, CellSize, worldPos);
			float velY = SampleBilinear(VelocitiesY, CellSize, worldPos);
			return new Vector2(velX, velY);
		}

		public float CalculateVelocityDivergenceAtCell(int cellX, int cellY)
		{
			// Get velocities at each edge of cell
			float velocityTop = VelocitiesY[cellX + 0, cellY + 1];
			float velocityLeft = VelocitiesX[cellX + 0, cellY + 0];
			float velocityRight = VelocitiesX[cellX + 1, cellY + 0];
			float velocityBottom = VelocitiesY[cellX + 0, cellY + 0];

			// Calculate how fast the fluid's velocity is changing across this cell on either axis
			float gradientX = (velocityRight - velocityLeft) / CellSize; // finite-difference approximation of ∂u/∂x
			float gradientY = (velocityTop - velocityBottom) / CellSize; // finite-difference approximation of ∂u/∂y
			// Sum to calculate if more fluid is entering (divergence < 0) or exiting the cell (divergence > 0)
			float divergence = gradientX + gradientY;
			return divergence;
		}


		bool IsSolid(int x, int y)

		{
			return SolidCellMap[Mathf.Clamp(x, 0, CellCountX - 1), Mathf.Clamp(y, 0, CellCountY - 1)];
		}

		float GetPressure(int x, int y)
		{
			return PressureMap[Mathf.Clamp(x, 0, CellCountX - 1), Mathf.Clamp(y, 0, CellCountY - 1)];
		}


		public void ClearDye()
		{
			for (int x = 0; x < CellCountX; x++)
			{
				for (int y = 0; y < CellCountY; y++)
				{
					SmokeMap[x, y] = 0;
				}
			}
		}

		public void ClearVelocities()
		{
			for (int x = 0; x < VelocitiesX.GetLength(0); x++)
			{
				for (int y = 0; y < VelocitiesX.GetLength(1); y++)
				{
					VelocitiesX[x, y] = 0;
				}
			}

			for (int x = 0; x < VelocitiesY.GetLength(0); x++)
			{
				for (int y = 0; y < VelocitiesY.GetLength(1); y++)
				{
					VelocitiesY[x, y] = 0;
				}
			}

			// pressure
			for (int x = 0; x < CellCountX; x++)
			{
				for (int y = 0; y < CellCountY; y++)
				{
					PressureMap[x, y] = 0;
				}
			}
		}

		void PreparePressureSolver()
		{
			for (int x = 0; x < CellCountX; x++)
			{
				for (int y = 0; y < CellCountY; y++)
				{
					int flowTop = IsSolid(x + 0, y + 1) ? 0 : 1;
					int flowLeft = IsSolid(x - 1, y + 0) ? 0 : 1;
					int flowRight = IsSolid(x + 1, y + 0) ? 0 : 1;
					int flowBottom = IsSolid(x + 0, y - 1) ? 0 : 1;
					int fluidEdgeCount = flowLeft + flowRight + flowTop + flowBottom;
					bool isSolid = IsSolid(x, y);

					float velocityTop = VelocitiesY[x + 0, y + 1];
					float velocityLeft = VelocitiesX[x + 0, y + 0];
					float velocityRight = VelocitiesX[x + 1, y + 0];
					float velocityBottom = VelocitiesY[x + 0, y + 0];

					float velTerm = (velocityRight - velocityLeft + velocityTop - velocityBottom) / TimeStep;

					PressureSolveDataMap[x, y] = new PressureSolveData()
					{
						flowLeft = flowLeft,
						flowRight = flowRight,
						flowTop = flowTop,
						flowBottom = flowBottom,
						isSolid = isSolid,
						flowEdgeCount = fluidEdgeCount,
						velocityTerm = velTerm
					};
				}
			}
		}

		struct PressureSolveData
		{
			public float flowLeft;
			public float flowRight;
			public float flowTop;
			public float flowBottom;
			public int flowEdgeCount;
			public bool isSolid;
			public float velocityTerm;
		}
	}
}