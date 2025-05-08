using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Float : MonoBehaviour
{
    public float speed = 0.01f, height = 0.6f, rotSpeed = 5.0f;

    private float px, py, pz, oy, rx, ry, rz, rw;
    
    private bool goingUp = true;

    void Start()
    {
        oy = transform.position.y;
    }

    void Update()
    {
        px = transform.position.x;
        py = transform.position.y;
        pz = transform.position.y;

        rx = transform.rotation.x;
        ry = transform.rotation.y;
        rz = transform.rotation.z;
        rw = transform.rotation.w;
        if (goingUp)
        {
            py = py + speed;

            if(py > (oy+(height / 2)))
            {
                goingUp = false;
            }
        }
        else
        {
            py = py - speed;

            if (py < (oy - (height / 2)))
            {
                goingUp = true;
            }
        }
        
        ry = ry + rotSpeed;

        //if (ry >= 179)
        //{
        //    ry = 0;
        //}

        this.transform.position = new Vector3(px, py, pz);
        this.transform.rotation = new Quaternion(rx, ry, rz, rw);

    }
}
