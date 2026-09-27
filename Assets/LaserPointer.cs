
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using System;

public class LaserPointer : MonoBehaviour
{
	public GameObject laserPrefab;
	public Vector3 startIndex;
	public List<Vector3> laserRoute;


    void Start()
    {
        StartCoroutine(ShootLaser());
    }

	public IEnumerator ShootLaser()
    {
        yield return new WaitForSeconds(3f);
        Vector3 startPosition = transform.position;
		foreach(Vector3 targetPoint in laserRoute)
		{
			float flag = 0f;
			GameObject laserObject = Instantiate(laserPrefab);
            laserObject.transform.position = startPosition;
            laserObject.transform.localScale = Vector3.zero;

            Vector3 distanceVector = targetPoint - startIndex;
            int distance = 0;

            if (distanceVector.x != 0)
            {
                distance = (int)distanceVector.x;
                laserObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                startPosition = startPosition + new Vector3(0f, 0, distance);
            }
            else if (distanceVector.y != 0)
            {
                distance = (int)distanceVector.y;
                laserObject.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                startPosition = startPosition + new Vector3(0f, distance, 0f);
            }
            else if (distanceVector.z != 0)
            {
                distance = (int)distanceVector.z;
                laserObject.transform.rotation = Quaternion.Euler(0f, 0f, -90f);
                startPosition = startPosition + new Vector3(distance, 0, 0);
            }

            while (flag < Math.Abs(distance))
			{
				laserObject.transform.localScale = new Vector3(1f, flag / 2, 1f);
				yield return null;
				flag += Time.deltaTime * 2;
			}
			startIndex = targetPoint;
		}
	}
}
