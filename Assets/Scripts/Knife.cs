using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Burst;
using UnityEngine;

public class Knife : MonoBehaviour
{
	[SerializeField] private float baseRadius = 0.1f;
	const float radiusChangeCoef = 0.1f;

	[SerializeField] private float timeBetweenCuts;
	[SerializeField] private float speedUp;
	[SerializeField] private Vector3 cutNormal;
	[SerializeField] private float topPosition;
	[SerializeField] private float lowestAllowedKnifePosition;


	private float lowestKnifePosition;
	private bool startedCut;
	private bool cutting;
	private bool raisingKnife;
	private float _radius;
	private float _radiusChange;
	private readonly List<GameObject> objectsToBend = new();
	private readonly List<Mesh> meshesToBend = new();
	private readonly List<List<Vector3>> _baseVertices = new();



	public GameObject[] objectsToCut;
	public Transform transformToMove;
	public static Knife Instance { get; private set; }


	private void Awake()
	{
		Instance = this;
		lowestKnifePosition = transform.position.y;
	}

	void Update()
	{
		if (raisingKnife)
		{
			MoveUp();
			return;
		}
		if (Input.anyKey)
		{
			if (!(startedCut || cutting))
			{
				ObjectToCut.Instance.CanMove = false;
				startedCut = true;
				lowestKnifePosition = transform.position.y;
				StartCuttingObjects();

			}
			else
			{
				if (transform.position.y < lowestKnifePosition)
				{
					lowestKnifePosition = transform.position.y;
					StartRolling();
				}
				MoveDown();

			}
		}
		else
		{
			MoveUp();
		}
	}

	async void StartCuttingObjects()
	{
		List<Task<TaskResult>> tasks = new();
		foreach (var g in objectsToCut)
		{
			Vector3 position = transform.position;
			Vector3 originalPos = g.transform.position;
			Vector3 originalScale = g.transform.localScale;
			Quaternion originalRot = g.transform.rotation;
			var mf = g.transform.GetComponent<MeshFilter>();
			var mr = g.transform.GetComponent<MeshRenderer>();
			var mesh = mf.mesh;
			var subMeshCount = mesh.subMeshCount;
			var triangles = new List<int[]>();
			var vertices = mesh.vertices;
			var normals = mesh.normals;
			var uv = mesh.uv;
			var mats = mr.materials;
			for (int i = 0; i < subMeshCount; i++)
			{
				triangles.Add(mesh.GetTriangles(i));
			}
			Plane cutPlane = new(g.transform.InverseTransformDirection(cutNormal), g.transform.InverseTransformPoint(position));
			var t = Task.Run(() => Cutter.AsyncCut(cutPlane,
				originalPos,
				originalRot,
				originalScale,
			mf,
			mr,
				mesh
				, subMeshCount,
				triangles,
				vertices,
				normals,
				uv,
				mats,
				g
			));
			tasks.Add(t);

		}
		await
		WaitForEndOfCut(tasks);
		}

	public async Task WaitForEndOfCut(List<Task<TaskResult>> tasks)
	{
		await Task.WhenAll(tasks);
		DealWithCutResult(tasks);

}

	void DealWithCutResult(List<Task<TaskResult>> tasks)
	{
		float minZ = 999999f;
		float maxZ = -999999f;
		foreach (var t in tasks)
		{
			var result = t.Result;
			if (result is null)
			{
				continue;
			}
			var ob = DealWithTaskResult(result);
			objectsToBend.Add(ob);
			meshesToBend.Add(ob.GetComponent<MeshFilter>().mesh);
			_baseVertices.Add(new());
			foreach (var v in meshesToBend[^1].vertices)
			{
				_baseVertices[^1].Add(new Vector3(v.x, v.y, v.z));
			}
			foreach (var p in _baseVertices[^1])
			{
				var worldPos = ob.transform.TransformPoint(p);

				if (worldPos.z > maxZ)
					maxZ = worldPos.z;
				if (worldPos.z < minZ)
					minZ = worldPos.z;
			}
		}
		tasks.Clear();
		float width = maxZ - minZ;
		_radius = 6 * width + baseRadius;
		_radiusChange = _radius * radiusChangeCoef;
		cutting = true;
	}

	IEnumerator AllowToStartCuttingIn(float timeInSeconds)
	{
		yield return new WaitForSeconds(timeInSeconds);
		raisingKnife = false;

	}

	private void MoveUp()
	{
		if (transform.position.y >= topPosition)
		{
			if (!startedCut)
			{
				ObjectToCut.Instance.CanMove = true;
				StartCoroutine(AllowToStartCuttingIn(timeBetweenCuts));
			}
			return;
		}
		transform.position += speedUp * Time.deltaTime * Vector3.up;
	}

	private void MoveDown()
	{
		transform.position += speedUp * Time.deltaTime * Vector3.down;
		if (transform.position.y <= lowestAllowedKnifePosition)
		{
			startedCut = false;
			cutting = false;
			raisingKnife = true;
			if (objectsToBend.Count > 0)
			{
				var parent = objectsToBend[0];
				for (int i = 1; i < objectsToBend.Count; i++)
				{
					objectsToBend[i].transform.SetParent(parent.transform);
				}
				var rb = parent.AddComponent<Rigidbody>();
				rb.AddRelativeForce(100 * Vector3.back);
				rb.AddTorque(50 * Vector3.left);
				foreach (var obj in objectsToBend)
				{
					Destroy(obj, 1);
				}
				objectsToBend.Clear();
				meshesToBend.Clear();
				_baseVertices.Clear();
			}
		}
	}



	private void StartRolling()
	{
		for (int i = 0; i < objectsToBend.Count; ++i)
		{
			Deform(objectsToBend[i], meshesToBend[i], _baseVertices[i]);
		}


	}
	private void Deform(GameObject g, Mesh mesh, List<Vector3> _vertices)
	{
		var newVerts = new List<Vector3>();
		for (var i = 0; i < _vertices.Count; i++)
		{
			var position = g.transform.TransformPoint(_vertices[i]);
			if (position.y >= lowestKnifePosition)
			{
				position = CalculateDisplacement(position - transform.position, _radius, _radiusChange);
				newVerts.Add(g.transform.InverseTransformPoint(position + transform.position));
			}
			else
			{
				newVerts.Add(g.transform.InverseTransformPoint(position));

			}
		}

		// MarkDynamic optimizes mesh for frequent updates according to docs
		mesh.MarkDynamic();
		// Update the mesh visually just by setting the new vertices array
		mesh.SetVertices(newVerts);
		// Must be called so the updated mesh is correctly affected by the light
		mesh.RecalculateNormals();
	}


	GameObject DealWithTaskResult(TaskResult result)
	{
		var rightMesh = result.rightMesh.GetGeneratedMesh();
		var leftMesh = result.leftMesh.GetGeneratedMesh();
		result.originalObject.GetComponent<MeshFilter>().mesh = leftMesh;
		Material[] mats = new Material[leftMesh.subMeshCount];
		for (int i = 0; i < leftMesh.subMeshCount; i++)
		{
			mats[i] = result.originalMeshRenderer.material;
		}
		result.originalMeshRenderer.materials = mats;

		GameObject right = new();
		right.transform.SetPositionAndRotation(result.position, result.rotation);
		right.transform.localScale = result.scale;
		var rightMeshRenderer = right.AddComponent<MeshRenderer>();

		mats = new Material[rightMesh.subMeshCount];
		for (int i = 0; i < rightMesh.subMeshCount ; i++)
		{
			mats[i] = result.materials[i%result.materials.Length];
		}
		rightMeshRenderer.materials = mats;
		right.AddComponent<MeshFilter>().mesh = rightMesh;

		return right;
	}


	[BurstCompile]
	public static Vector3 CalculateDisplacement(Vector3 basePosition, float initialRadius, float radiusDecrease)
	{
		Vector3 result = new(basePosition.x, 0, -initialRadius);
		float y = basePosition.y;
		while (y > initialRadius)
		{
			y -= 2 * Mathf.PI * initialRadius;
			initialRadius -= radiusDecrease;
		}
		float angle = (y / initialRadius);
		var curradius = initialRadius + basePosition.z;
		result += new Vector3(0, Mathf.Sin(angle) * curradius, Mathf.Cos(angle) * curradius);
		return result;
	}
}
