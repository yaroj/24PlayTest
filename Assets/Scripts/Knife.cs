using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Burst;
using UnityEngine;

public class Knife : MonoBehaviour
{
	const float baseRadius = 0.25f;
	const float radiusChangeCoef = 0.1f;

	[SerializeField] private float speedDown;
	[SerializeField] private float speedUp;
	[SerializeField] private Vector3 cutPosition;
	[SerializeField] private Vector3 cutNormal;
	[SerializeField] private float topPosition;
	[SerializeField] private float lowestAllowedKnifePosition;


	private float lowestKnifePosition;
	private bool startedCut;
	private bool cutting;
	private bool raisingKnife;
	private float _radius;
	private float _radiusChange;
	private List<GameObject> objectsToBend = new();
	private List<Mesh> meshesToBend = new();
	private List<List<Vector3>> _baseVertices = new();

	private List<Task<Cutter.TaskResult>> tasks = new();

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
		if (!(startedCut || cutting) && Input.anyKeyDown)
		{
			ObjectToCut.Instance.CanMove = false;
			startedCut = true;
			lowestKnifePosition = transform.position.y;
			StartCoroutine(StartCuttingObjects());

		}
		else if (cutting && Input.anyKey)
		{
			if (transform.position.y < lowestKnifePosition)
			{
				lowestKnifePosition = transform.position.y;
				StartRolling();
			}
			MoveDown();

		}
		else
		{
			MoveUp();
		}
	}

	IEnumerator StartCuttingObjects()
	{
		print("about to start cutting " + objectsToCut.Length);
		foreach (var g in objectsToCut)
		{
			print(g);
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
			//var t =( Cutter.Cut(g, transform.position, cutNormal));
			tasks.Add(t);

		}
		print("waiting starts");
		for (int i = 0; i < 50; i++)
		{
			print(i);
			yield return new WaitForSeconds(0.1f);
			bool completed = true;
			foreach (var t in tasks)
			{
				if (!t.IsCompleted)
				{
					completed = false;
					break;
				}
			}
			if (completed)
			{
				print("ended with " + i);
				EndCuttingObjects();
				yield break;
			}
		}
		Debug.LogError("it took too long");
	}

	void EndCuttingObjects()
	{
		float minZ = 999999f;
		float maxZ = -999999f;
		print("about to end cutting " + objectsToCut.Length);
		foreach (var t in tasks)
		{
			var result = t.Result;
			if (result is null)
			{
				print("continued");
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
			//print(_baseVertices[^1].Count + "   " + meshesToBend[^1].vertices.Length);
			foreach (var p in _baseVertices[^1])
			{
				var worldPos = ob.transform.TransformPoint(p);

				if (worldPos.z > maxZ)
					maxZ = worldPos.z;
				if (worldPos.z < minZ)
					minZ = worldPos.z;
				// 				Plane plane = new Plane();
				// Vector3 transformedStartingPoint = g.transform.InverseTransformPoint(transform.position);
				// 		Vector3 transformedNormal = ((Vector3)(g.transform.localToWorldMatrix.transpose * cutNormal)).normalized;
				// plane.SetNormalAndPosition(
				// 		transformedNormal, // transformedNormal,
				// 		transformedStartingPoint);
				// 		GameObject[] slices = Assets.Scripts.Slicer.Slice(plane, g);
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
				StartCoroutine(AllowToStartCuttingIn(0.2f));
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
			Debug.LogError("finished cut");
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


	GameObject DealWithTaskResult(Cutter.TaskResult result)
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
		right.AddComponent<MeshRenderer>();

		mats = new Material[rightMesh.subMeshCount];
		for (int i = 0; i < rightMesh.subMeshCount && i < result.materials.Length; i++)
		{
			mats[i] = result.materials[i];
		}
		right.GetComponent<MeshRenderer>().materials = mats;
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
		float angle = (y / initialRadius /* 2 * Mathf.PI*/);
		var curradius = initialRadius + basePosition.z;
		result += new Vector3(0, Mathf.Sin(angle) * curradius, Mathf.Cos(angle) * curradius);
		return result;
	}
}
