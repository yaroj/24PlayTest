using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine;

public class Cutter
{
	private static Mesh originalMesh;


	public class TaskResult
	{
		public GeneratedMesh leftMesh;
		public GeneratedMesh rightMesh;
		public Vector3 position;
		public Quaternion rotation;
		public Vector3 scale;
		public Material[] materials;
		public MeshFilter originalMeshFilter;
		public MeshRenderer originalMeshRenderer;
		public GameObject originalObject;
		public TaskResult(GeneratedMesh leftMesh,
			GeneratedMesh rightMesh,
			Vector3 position,
			Quaternion rotation,
			Vector3 scale,
			Material[] materials
			, MeshFilter originalMeshFilter
			, MeshRenderer originalMeshRenderer
			, GameObject originalObject
			)
		{
			this.leftMesh = leftMesh;
			this.rightMesh = rightMesh;
			this.position = position;
			this.rotation = rotation;
			this.scale = scale;
			this.materials = materials;
			this.originalMeshFilter = originalMeshFilter;
			this.originalMeshRenderer = originalMeshRenderer;
			this.originalObject = originalObject;
		}
	}

	public static TaskResult AsyncCut(Plane cutPlane,
		Vector3 originalPosition,
		Quaternion originalRotation,
		Vector3 originalScale,
		MeshFilter originalMeshFilter, MeshRenderer originalMeshRenderer,
		Mesh mesh,
		int subMeshCount,
		List<int[]> triangles
		, Vector3[] vertices
		, Vector3[] normals
		, Vector2[] uv,
		Material[] materials
		, GameObject originalObject
		)
	{

		originalMesh = mesh;

		if (originalMesh == null)
		{
			return null;
		}


		List<Vector3> addedVertices = new();
		GeneratedMesh leftMesh = new();
		GeneratedMesh rightMesh = new();

		SeparateMeshes(leftMesh, rightMesh, cutPlane, addedVertices, subMeshCount, triangles, ref vertices, ref normals, ref uv);
		if (addedVertices.Count == 0)
		{
			if (rightMesh.Vertices.Count == 0)
				return null;
		}
		Fill(addedVertices, cutPlane, leftMesh, rightMesh, subMeshCount);

		PrepareMeshForSpiralling(rightMesh, ref vertices, ref normals, ref uv);

		return new TaskResult(leftMesh, rightMesh, originalPosition, originalRotation, originalScale, materials, originalMeshFilter, originalMeshRenderer, originalObject);
	}

	[BurstCompile]
	private static void PrepareMeshForSpiralling(GeneratedMesh meshToSpiral, ref Vector3[] vertices, ref Vector3[] normals, ref Vector2[] uv)
	{
		var highestVert = Vector3.down * 99999999;
		var lowestVert = Vector3.up * 99999999;
		foreach (var x in vertices)
		{
			if (x.y < lowestVert.y)
				lowestVert = x;

			if (x.y > highestVert.y)
				lowestVert = x;

		}
		float step = 0.05f;

		for (float currentHeight = lowestVert.y + step; currentHeight < highestVert.y; currentHeight += step)
		{
			SliceMeshWithoutSeparating(new Plane(Vector3.up, new Vector3(0, currentHeight, 0)), meshToSpiral,
				ref vertices, ref normals, ref uv

				);
		}
		return;
		
	}


	private static void SliceMeshWithoutSeparating(Plane plane, GeneratedMesh leftMesh, ref Vector3[] vertices, ref Vector3[] normals, ref Vector2[] uv)
	{
		for (int i = 0; i < originalMesh.subMeshCount; i++)
		{
			var subMeshIndices = originalMesh.GetTriangles(i);

			//We are now going through the submesh indices as triangles to determine on what side of the mesh they are.
			for (int j = 0; j < subMeshIndices.Length; j += 3)
			{
				var triangleIndexA = subMeshIndices[j];
				var triangleIndexB = subMeshIndices[j + 1];
				var triangleIndexC = subMeshIndices[j + 2];

				MeshTriangle currentTriangle = GetTriangle(triangleIndexA, triangleIndexB, triangleIndexC, i, ref vertices, ref normals, ref uv);

				//We are now using the plane.getside function to see on which side of the cut our trianle is situated 
				//or if it might be cut through
				bool triangleALeftSide = plane.GetSide(originalMesh.vertices[triangleIndexA]);
				bool triangleBLeftSide = plane.GetSide(originalMesh.vertices[triangleIndexB]);
				bool triangleCLeftSide = plane.GetSide(originalMesh.vertices[triangleIndexC]);

				switch (triangleALeftSide)
				{
					//All three vertices are on the left side of the plane, so they need to be added to the left
					//mesh
					case true when triangleBLeftSide && triangleCLeftSide:
						break;
					//All three vertices are on the right side of the mesh.
					case false when !triangleBLeftSide && !triangleCLeftSide:
						break;
					default:
						CutTriangleWithoutMeshChange(plane, currentTriangle, triangleALeftSide, triangleBLeftSide, triangleCLeftSide, leftMesh);
						break;
				}
			}
		}
	}

	/// <summary>
	/// Iterates over all the triangles of all the submeshes of the original mesh to separate the left
	/// and right side of the plane into individual meshes.
	/// </summary>
	/// <param name="leftMesh"></param>
	/// <param name="rightMesh"></param>
	/// <param name="plane"></param>
	/// <param name="addedVertices"></param>
	private static void SeparateMeshes(GeneratedMesh leftMesh, GeneratedMesh rightMesh, Plane plane, List<Vector3> addedVertices, int subMeshCount,
		List<int[]> triangles, ref Vector3[] vertices, ref Vector3[] normals, ref Vector2[] uv)
	{
		for (int i = 0; i < subMeshCount; i++)
		{
			var subMeshIndices = triangles[i];

			//We are now going through the submesh indices as triangles to determine on what side of the mesh they are.
			for (int j = 0; j < subMeshIndices.Length; j += 3)
			{
				var triangleIndexA = subMeshIndices[j];
				var triangleIndexB = subMeshIndices[j + 1];
				var triangleIndexC = subMeshIndices[j + 2];

				MeshTriangle currentTriangle = GetTriangle(triangleIndexA, triangleIndexB, triangleIndexC, i, ref vertices, ref normals, ref uv);

				//We are now using the plane.getside function to see on which side of the cut our trianle is situated 
				//or if it might be cut through
				bool triangleALeftSide = plane.GetSide(vertices[triangleIndexA]);
				bool triangleBLeftSide = plane.GetSide(vertices[triangleIndexB]);
				bool triangleCLeftSide = plane.GetSide(vertices[triangleIndexC]);

				switch (triangleALeftSide)
				{
					//All three vertices are on the left side of the plane, so they need to be added to the left
					//mesh
					case true when triangleBLeftSide && triangleCLeftSide:
						leftMesh.AddTriangle(currentTriangle);
						break;
					//All three vertices are on the right side of the mesh.
					case false when !triangleBLeftSide && !triangleCLeftSide:
						rightMesh.AddTriangle(currentTriangle);
						break;
					default:
						CutTriangle(plane, currentTriangle, triangleALeftSide, triangleBLeftSide, triangleCLeftSide, leftMesh, rightMesh, addedVertices);
						break;
				}
			}
		}
	}

	/// <summary>
	/// Returns the tree vertices of a triangle as one MeshTriangle to keep code more readable
	/// </summary>
	/// <param name="_triangleIndexA"></param>
	/// <param name="_triangleIndexB"></param>
	/// <param name="_triangleIndexC"></param>
	/// <param name="_submeshIndex"></param>
	/// <returns></returns>
	private static MeshTriangle GetTriangle(int _triangleIndexA, int _triangleIndexB, int _triangleIndexC,
		int _submeshIndex,
		ref Vector3[] vertices, ref Vector3[] normals, ref Vector2[] uv)
	{
		//Adding the Vertices at the triangleIndex
		Vector3[] verticesToAdd = {
			vertices[_triangleIndexA],
			vertices[_triangleIndexB],
			vertices[_triangleIndexC]
		};

		//Adding the normals at the triangle index
		Vector3[] normalsToAdd = {
			normals[_triangleIndexA],
			normals[_triangleIndexB],
			normals[_triangleIndexC]
		};

		//adding the uvs at the triangleIndex
		Vector2[] uvsToAdd = {
			uv[_triangleIndexA],
			uv[_triangleIndexB],
			uv[_triangleIndexC]
		};

		return new MeshTriangle(verticesToAdd, normalsToAdd, uvsToAdd, _submeshIndex);
	}

	/// <summary>
	/// Cuts a triangle that exists between both sides of the cut apart adding additional vertices
	/// where needed to create intact triangles on both sides.
	/// </summary>
	/// <param name="plane"></param>
	/// <param name="triangle"></param>
	/// <param name="triangleALeftSide"></param>
	/// <param name="triangleBLeftSide"></param>
	/// <param name="triangleCLeftSide"></param>
	/// <param name="leftMesh"></param>
	/// <param name="rightMesh"></param>
	/// <param name="addedVertices"></param>
	private static void CutTriangle(Plane plane, MeshTriangle triangle, bool triangleALeftSide, bool triangleBLeftSide, bool triangleCLeftSide,
	GeneratedMesh leftMesh, GeneratedMesh rightMesh, List<Vector3> addedVertices)
	{
		List<bool> leftSide = new()
		{
			triangleALeftSide,
			triangleBLeftSide,
			triangleCLeftSide
		};

		MeshTriangle leftMeshTriangle = new(new Vector3[2], new Vector3[2], new Vector2[2], triangle.SubmeshIndex);
		MeshTriangle rightMeshTriangle = new(new Vector3[2], new Vector3[2], new Vector2[2], triangle.SubmeshIndex);

		bool left = false;
		bool right = false;

		for (int i = 0; i < 3; i++)
		{
			if (leftSide[i])
			{
				if (!left)
				{
					left = true;

					leftMeshTriangle.Vertices[0] = triangle.Vertices[i];
					leftMeshTriangle.Vertices[1] = leftMeshTriangle.Vertices[0];

					leftMeshTriangle.UVs[0] = triangle.UVs[i];
					leftMeshTriangle.UVs[1] = leftMeshTriangle.UVs[0];

					leftMeshTriangle.Normals[0] = triangle.Normals[i];
					leftMeshTriangle.Normals[1] = leftMeshTriangle.Normals[0];
				}
				else
				{
					leftMeshTriangle.Vertices[1] = triangle.Vertices[i];
					leftMeshTriangle.Normals[1] = triangle.Normals[i];
					leftMeshTriangle.UVs[1] = triangle.UVs[i];
				}
			}
			else
			{
				if (!right)
				{
					right = true;

					rightMeshTriangle.Vertices[0] = triangle.Vertices[i];
					rightMeshTriangle.Vertices[1] = rightMeshTriangle.Vertices[0];

					rightMeshTriangle.UVs[0] = triangle.UVs[i];
					rightMeshTriangle.UVs[1] = rightMeshTriangle.UVs[0];

					rightMeshTriangle.Normals[0] = triangle.Normals[i];
					rightMeshTriangle.Normals[1] = rightMeshTriangle.Normals[0];

				}
				else
				{
					rightMeshTriangle.Vertices[1] = triangle.Vertices[i];
					rightMeshTriangle.Normals[1] = triangle.Normals[i];
					rightMeshTriangle.UVs[1] = triangle.UVs[i];
				}
			}
		}

		float normalizedDistance;
		plane.Raycast(new Ray(leftMeshTriangle.Vertices[0], (rightMeshTriangle.Vertices[0] - leftMeshTriangle.Vertices[0]).normalized), out float distance);

		normalizedDistance = distance / (rightMeshTriangle.Vertices[0] - leftMeshTriangle.Vertices[0]).magnitude;
		Vector3 vertLeft = Vector3.Lerp(leftMeshTriangle.Vertices[0], rightMeshTriangle.Vertices[0], normalizedDistance);
		addedVertices.Add(vertLeft);

		Vector3 normalLeft = Vector3.Lerp(leftMeshTriangle.Normals[0], rightMeshTriangle.Normals[0], normalizedDistance);
		Vector2 uvLeft = Vector2.Lerp(leftMeshTriangle.UVs[0], rightMeshTriangle.UVs[0], normalizedDistance);

		plane.Raycast(new Ray(leftMeshTriangle.Vertices[1], (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]).normalized), out distance);

		normalizedDistance = distance / (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]).magnitude;
		Vector3 vertRight = Vector3.Lerp(leftMeshTriangle.Vertices[1], rightMeshTriangle.Vertices[1], normalizedDistance);
		addedVertices.Add(vertRight);

		Vector3 normalRight = Vector3.Lerp(leftMeshTriangle.Normals[1], rightMeshTriangle.Normals[1], normalizedDistance);
		Vector2 uvRight = Vector2.Lerp(leftMeshTriangle.UVs[1], rightMeshTriangle.UVs[1], normalizedDistance);

		//TESTING OUR FIRST TRIANGLE
		MeshTriangle currentTriangle;
		Vector3[] updatedVertices = { leftMeshTriangle.Vertices[0], vertLeft, vertRight };
		Vector3[] updatedNormals = { leftMeshTriangle.Normals[0], normalLeft, normalRight };
		Vector2[] updatedUVs = { leftMeshTriangle.UVs[0], uvLeft, uvRight };

		currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);

		//If our vertices ant the same
		if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
		{
			if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
			{
				FlipTriangel(currentTriangle);
			}
			leftMesh.AddTriangle(currentTriangle);
		}

		//SECOND TRIANGLE 
		updatedVertices = new Vector3[] { leftMeshTriangle.Vertices[0], leftMeshTriangle.Vertices[1], vertRight };
		updatedNormals = new Vector3[] { leftMeshTriangle.Normals[0], leftMeshTriangle.Normals[1], normalRight };
		updatedUVs = new Vector2[] { leftMeshTriangle.UVs[0], leftMeshTriangle.UVs[1], uvRight };


		currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);
		//If our vertices arent the same
		if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
		{
			if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
			{
				FlipTriangel(currentTriangle);
			}
			leftMesh.AddTriangle(currentTriangle);
		}

		//THIRD TRIANGLE 
		updatedVertices = new Vector3[] { rightMeshTriangle.Vertices[0], vertLeft, vertRight };
		updatedNormals = new Vector3[] { rightMeshTriangle.Normals[0], normalLeft, normalRight };
		updatedUVs = new Vector2[] { rightMeshTriangle.UVs[0], uvLeft, uvRight };

		currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);
		//If our vertices arent the same
		if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
		{
			if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
			{
				FlipTriangel(currentTriangle);
			}
			rightMesh.AddTriangle(currentTriangle);
		}

		//FOURTH TRIANGLE 
		updatedVertices = new Vector3[] { rightMeshTriangle.Vertices[0], rightMeshTriangle.Vertices[1], vertRight };
		updatedNormals = new Vector3[] { rightMeshTriangle.Normals[0], rightMeshTriangle.Normals[1], normalRight };
		updatedUVs = new Vector2[] { rightMeshTriangle.UVs[0], rightMeshTriangle.UVs[1], uvRight };

		currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);
		//If our vertices arent the same
		if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
		{
			if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
			{
				FlipTriangel(currentTriangle);
			}
			rightMesh.AddTriangle(currentTriangle);
		}
	}

	private static void CutTriangleWithoutMeshChange(Plane plane, MeshTriangle triangle, bool triangleALeftSide, bool triangleBLeftSide, bool triangleCLeftSide,
	GeneratedMesh leftMesh)
	{
		List<bool> leftSide = new()
		{
			triangleALeftSide,
			triangleBLeftSide,
			triangleCLeftSide
		};

		MeshTriangle leftMeshTriangle = new(new Vector3[2], new Vector3[2], new Vector2[2], triangle.SubmeshIndex);
		MeshTriangle rightMeshTriangle = new(new Vector3[2], new Vector3[2], new Vector2[2], triangle.SubmeshIndex);

		bool left = false;
		bool right = false;

		for (int i = 0; i < 3; i++)
		{
			if (leftSide[i])
			{
				if (!left)
				{
					left = true;

					leftMeshTriangle.Vertices[0] = triangle.Vertices[i];
					leftMeshTriangle.Vertices[1] = leftMeshTriangle.Vertices[0];

					leftMeshTriangle.UVs[0] = triangle.UVs[i];
					leftMeshTriangle.UVs[1] = leftMeshTriangle.UVs[0];

					leftMeshTriangle.Normals[0] = triangle.Normals[i];
					leftMeshTriangle.Normals[1] = leftMeshTriangle.Normals[0];
				}
				else
				{
					leftMeshTriangle.Vertices[1] = triangle.Vertices[i];
					leftMeshTriangle.Normals[1] = triangle.Normals[i];
					leftMeshTriangle.UVs[1] = triangle.UVs[i];
				}
			}
			else
			{
				if (!right)
				{
					right = true;

					rightMeshTriangle.Vertices[0] = triangle.Vertices[i];
					rightMeshTriangle.Vertices[1] = rightMeshTriangle.Vertices[0];

					rightMeshTriangle.UVs[0] = triangle.UVs[i];
					rightMeshTriangle.UVs[1] = rightMeshTriangle.UVs[0];

					rightMeshTriangle.Normals[0] = triangle.Normals[i];
					rightMeshTriangle.Normals[1] = rightMeshTriangle.Normals[0];

				}
				else
				{
					rightMeshTriangle.Vertices[1] = triangle.Vertices[i];
					rightMeshTriangle.Normals[1] = triangle.Normals[i];
					rightMeshTriangle.UVs[1] = triangle.UVs[i];
				}
			}
		}

		float normalizedDistance;
		plane.Raycast(new Ray(leftMeshTriangle.Vertices[0], (rightMeshTriangle.Vertices[0] - leftMeshTriangle.Vertices[0]).normalized), out float distance);

		normalizedDistance = distance / (rightMeshTriangle.Vertices[0] - leftMeshTriangle.Vertices[0]).magnitude;
		Vector3 vertLeft = Vector3.Lerp(leftMeshTriangle.Vertices[0], rightMeshTriangle.Vertices[0], normalizedDistance);

		Vector3 normalLeft = Vector3.Lerp(leftMeshTriangle.Normals[0], rightMeshTriangle.Normals[0], normalizedDistance);
		Vector2 uvLeft = Vector2.Lerp(leftMeshTriangle.UVs[0], rightMeshTriangle.UVs[0], normalizedDistance);

		plane.Raycast(new Ray(leftMeshTriangle.Vertices[1], (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]).normalized), out distance);

		normalizedDistance = distance / (rightMeshTriangle.Vertices[1] - leftMeshTriangle.Vertices[1]).magnitude;
		Vector3 vertRight = Vector3.Lerp(leftMeshTriangle.Vertices[1], rightMeshTriangle.Vertices[1], normalizedDistance);

		Vector3 normalRight = Vector3.Lerp(leftMeshTriangle.Normals[1], rightMeshTriangle.Normals[1], normalizedDistance);
		Vector2 uvRight = Vector2.Lerp(leftMeshTriangle.UVs[1], rightMeshTriangle.UVs[1], normalizedDistance);

		//TESTING OUR FIRST TRIANGLE
		MeshTriangle currentTriangle;
		Vector3[] updatedVertices = { leftMeshTriangle.Vertices[0], vertLeft, vertRight };
		Vector3[] updatedNormals = { leftMeshTriangle.Normals[0], normalLeft, normalRight };
		Vector2[] updatedUVs = { leftMeshTriangle.UVs[0], uvLeft, uvRight };

		currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);

		//If our vertices ant the same
		if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
		{
			if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
			{
				FlipTriangel(currentTriangle);
			}
			leftMesh.AddTriangle(currentTriangle);
		}

		//SECOND TRIANGLE 
		updatedVertices = new Vector3[] { leftMeshTriangle.Vertices[0], leftMeshTriangle.Vertices[1], vertRight };
		updatedNormals = new Vector3[] { leftMeshTriangle.Normals[0], leftMeshTriangle.Normals[1], normalRight };
		updatedUVs = new Vector2[] { leftMeshTriangle.UVs[0], leftMeshTriangle.UVs[1], uvRight };


		currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);
		//If our vertices arent the same
		if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
		{
			if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
			{
				FlipTriangel(currentTriangle);
			}
			leftMesh.AddTriangle(currentTriangle);
		}

		//THIRD TRIANGLE 
		updatedVertices = new Vector3[] { rightMeshTriangle.Vertices[0], vertLeft, vertRight };
		updatedNormals = new Vector3[] { rightMeshTriangle.Normals[0], normalLeft, normalRight };
		updatedUVs = new Vector2[] { rightMeshTriangle.UVs[0], uvLeft, uvRight };

		currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);
		//If our vertices arent the same
		if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
		{
			if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
			{
				FlipTriangel(currentTriangle);
			}
			leftMesh.AddTriangle(currentTriangle);
		}

		//FOURTH TRIANGLE 
		updatedVertices = new Vector3[] { rightMeshTriangle.Vertices[0], rightMeshTriangle.Vertices[1], vertRight };
		updatedNormals = new Vector3[] { rightMeshTriangle.Normals[0], rightMeshTriangle.Normals[1], normalRight };
		updatedUVs = new Vector2[] { rightMeshTriangle.UVs[0], rightMeshTriangle.UVs[1], uvRight };

		currentTriangle = new MeshTriangle(updatedVertices, updatedNormals, updatedUVs, triangle.SubmeshIndex);
		//If our vertices arent the same
		if (updatedVertices[0] != updatedVertices[1] && updatedVertices[0] != updatedVertices[2])
		{
			if (Vector3.Dot(Vector3.Cross(updatedVertices[1] - updatedVertices[0], updatedVertices[2] - updatedVertices[0]), updatedNormals[0]) < 0)
			{
				FlipTriangel(currentTriangle);
			}
			leftMesh.AddTriangle(currentTriangle);
		}
	}


	private static void FlipTriangel(MeshTriangle _triangle)
	{
		(_triangle.Vertices[0], _triangle.Vertices[2]) = (_triangle.Vertices[2], _triangle.Vertices[0]);
		(_triangle.Normals[0], _triangle.Normals[2]) = (_triangle.Normals[2], _triangle.Normals[0]);
		(_triangle.UVs[2], _triangle.UVs[0]) = (_triangle.UVs[0], _triangle.UVs[2]);
	}


	public static void EvaluatePairs(List<Vector3> _addedVertices, List<Vector3> _vertices, List<Vector3> _polygone)
	{
		bool isDone = false;
		while (!isDone)
		{
			isDone = true;
			for (int i = 0; i < _addedVertices.Count; i += 2)
			{
				if (_addedVertices[i] == _polygone[^1] && !_vertices.Contains(_addedVertices[i + 1]))
				{
					isDone = false;
					_polygone.Add(_addedVertices[i + 1]);
					_vertices.Add(_addedVertices[i + 1]);
				}
				else if (_addedVertices[i + 1] == _polygone[^1] && !_vertices.Contains(_addedVertices[i]))
				{
					isDone = false;
					_polygone.Add(_addedVertices[i]);
					_vertices.Add(_addedVertices[i]);
				}
			}
		}
	}
	private static bool PointInsideTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
	{
		return
			Mathf.Sign(SignedAngleBetween(b - a, point - a, normal)) *
			Mathf.Sign(SignedAngleBetween(c - b, point - b, normal)) >= 0 &&
			Mathf.Sign(SignedAngleBetween(c - b, point - b, normal)) *
			Mathf.Sign(SignedAngleBetween(a - c, point - c, normal)) >= 0;
	}
	private static float SignedAngleBetween(Vector3 a, Vector3 b, Vector3 normal)
	{
		// angle in [0,180]
		float angle = Vector3.Angle(a, b);
		float sign = Mathf.Sign(Vector3.Dot(normal, Vector3.Cross(a, b)));

		// angle in [-179,180]
		float signed_angle = angle * sign;

		// angle in [0,360] (not used but included here for completeness)
		//float angle360 =  (signed_angle + 180) % 360;

		return signed_angle;
	}

	static List< Vector3> RemoveDuplicates(List<Vector3> l)
	{
		const int lastBitRemover = (1 << 30) - 2;
		int IndexOfClosest(Vector3 value)
		{

			float closestDistance = 999999999999;
			int ans = -1;
			for (int i = 0; i < l.Count; i++)
			{

				if (Vector3.Distance(l[i], value) < closestDistance)
				{
					ans = i;
					closestDistance = Vector3.SqrMagnitude(l[i] - value);
				}

			}
			return ans;
		}
		List<Vector3> result = new();
		int j;
		for (int i = 0; i < l.Count; i++)
		{

			for (j = i + 1; j < l.Count; j++)
			{
				if (l[i] == l[j])
				{
					int cur = i ^ 1;
					while (l.Count > 0)
					{
						if (l.Count <= cur)
							break;
						try
						{
							result.Add(l[cur]);
							l.RemoveRange(cur & lastBitRemover, 2);
							if (l.Count == 0)
								return result;
							cur = l.FindIndex(x => x == result[^1]);
							if (cur == -1)
							{
								cur = IndexOfClosest(result[^1]);
							}
							cur ^= 1;
						}
						catch (Exception)
						{
							break;
						}
					}
					return result;
				}
			}
		}

		return new();
	}

	private static List<Vector3[]> GetTringlesForPlane(List<Vector3> _vertices, Plane _plane)
	{
		int[] previousFree = new int[_vertices.Count];
		int[] nextFree = new int[_vertices.Count];
		bool[] used = new bool[_vertices.Count];
		float totalAngle = 0;


		bool IsTherePointInsideTriangle(int Apoint, int Bpoint, int Cpoint, Vector3 normal)
		{
			for (int i = 0; i < _vertices.Count; i++)
			{
				if (i != Apoint && i != Bpoint && i != Cpoint &&
				PointInsideTriangle(_vertices[i], _vertices[Apoint], _vertices[Bpoint], _vertices[Cpoint], normal)
				)
				{
					return true;
				}
			}
			return false;
		}

		int GetNext(int current)
		{
			return (current + 1) % _vertices.Count;
		}

		int GetPrev(int current)
		{
			return (current - 1 + _vertices.Count) % _vertices.Count;
		}

		int GetPrevFree(int current)
		{
			if (used[previousFree[current]])
			{
				previousFree[current] = GetPrevFree(previousFree[current]);
			}
			return previousFree[current];
		}

		int GetNextFree(int current)
		{
			if (used[nextFree[current]])
			{
				nextFree[current] = GetNextFree(nextFree[current]);
			}
			return nextFree[current];
		}

		_vertices = RemoveDuplicates(_vertices);

		for (int i = 0; i < _vertices.Count; i++)
		{
			int nextIndex = GetNext(i);
			int prevIndex = GetPrev(i);
			previousFree[i] = prevIndex;
			nextFree[i] = nextIndex;
			var prev = _vertices[prevIndex];
			var cur = _vertices[i];
			var next = _vertices[nextIndex];
			var angle = SignedAngleBetween(next - cur, cur - prev, _plane.normal);
			totalAngle += angle;
		}

		Vector3 normal = _plane.normal;
		Vector2[] tempoUVS = { Vector2.zero, Vector2.zero, Vector2.zero };

		List<Vector3[]> triangles = new();

		while (triangles.Count < _vertices.Count - 2)
		{
			int triCount = triangles.Count;
			for (int i = 0; i < _vertices.Count; i++)
			{
				if (used[i])
					continue;
				var cur = _vertices[i];
				int prevIndex = GetPrevFree(i);
				var prev = _vertices[prevIndex];
				int nextIndex = GetNextFree(i);
				var next = _vertices[nextIndex];
				if (SignedAngleBetween(next - cur, cur - prev, normal) * totalAngle > 0)
				{
					if (!IsTherePointInsideTriangle(i, prevIndex, nextIndex, normal))
					{
						triangles.Add(
							new Vector3[] { prev, cur, next });
						used[i] = true;
					}
				}

			}
			if (triCount == triangles.Count)
				break;
		}
		return triangles;
	}

	private static void Fill(List<Vector3> _vertices, Plane _plane, GeneratedMesh _leftMesh, GeneratedMesh _rightMesh, int submeshCount)
	{
		var sw = Stopwatch.StartNew();
		var triangles = GetTringlesForPlane(_vertices, _plane);
		UnityEngine.Debug.Log(sw.ElapsedMilliseconds);
		foreach (var triangle in triangles)
		{
			Vector3[] vertices = triangle;
			Vector3[] normals = { -_plane.normal, -_plane.normal, -_plane.normal };
			Vector2[] uvs = { Vector2.zero, Vector2.zero, Vector2.zero };
			MeshTriangle currentTriangle = new(vertices, normals, uvs, submeshCount + 1);

			if (Vector3.Dot(Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]), normals[0]) < 0)
			{
				FlipTriangel(currentTriangle);
			}
			_leftMesh.AddTriangle(currentTriangle);

			normals = new[] { _plane.normal, _plane.normal, _plane.normal };
			currentTriangle = new MeshTriangle(vertices, normals, uvs, submeshCount + 1);

			if (Vector3.Dot(Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]), normals[0]) < 0)
			{
				FlipTriangel(currentTriangle);
			}
			_rightMesh.AddTriangle(currentTriangle);
		}
	}
}

