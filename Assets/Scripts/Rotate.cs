using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rotate : MonoBehaviour
{
    public int cur = 0;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        cur += 1;
        if(cur > 180)
        {
            cur = 0;
        }

        this.transform.rotation.Set(transform.rotation.x, cur, transform.rotation.z, transform.rotation.w);
    }
}
