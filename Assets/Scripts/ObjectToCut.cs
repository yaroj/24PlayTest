using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ObjectToCut : MonoBehaviour
{
	[SerializeField] private GameObject[] cuttableObjects;
	[SerializeField] private float speed = 0.5f;
	[SerializeField] private float distanceAfterObjectPassToRestart;
	private float ZofFinish;

	public bool CanMove = true;

	public static ObjectToCut Instance { get; private set; }

	void Start()
	{
		float distanceToFinish = 0;
		Instance = this;
		Knife.Instance.transformToMove = transform;
		Knife.Instance.objectsToCut = cuttableObjects;
		foreach (var cuttableObject in cuttableObjects)
		{
			var vertices = cuttableObject.GetComponent<MeshFilter>().mesh.vertices;
			foreach (var vertex in vertices)
			{
				distanceToFinish = Mathf.Max(distanceToFinish, (cuttableObject.transform.TransformPoint(vertex)).z - Knife.Instance.transform.position.z);
			}
		}
		ZofFinish = transform.position.z - distanceToFinish - distanceAfterObjectPassToRestart;
	}

	void Update()
	{
		if (CanMove)
		{
			transform.position += speed * Time.deltaTime * Vector3.back;
			if (transform.position.z < ZofFinish)
			{
				SceneManager.LoadScene(SceneManager.GetActiveScene().name);
			}
		}
	}
}
