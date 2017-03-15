using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class HypercubeExitChecker : MonoBehaviour
{

    public string launcherPath = "C:\\VolumeLauncher\\VolumeLauncher.exe";
    // Use this for initialization
    public float timer;

    public float beginOverlayTime = 1f;
    public float cornerMargin = .1f;
    MeshRenderer hypercubeMesh;
    public Texture[] exitTextPngs;

    public bool runningCoroutine = false;

    void Start()
    {
        hypercubeMesh = GameObject.Find("HypercubeCastMesh").transform.GetChild(0).GetComponent<MeshRenderer>();
        Material[] mats = hypercubeMesh.materials;
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            StartCoroutine("FadeInExitText");
        }
        if (hypercube.input.touchPanel == null) //Volume not connected via USB
            return;

        uint touches = hypercube.input.touchPanel.touchCount;
        if (touches >= 1)
        {
            bool touchThisFrame = false;

            for (int i = 0; i < touches; i++)
            {
                if (hypercube.input.touchPanel.touches[0].posX < cornerMargin && hypercube.input.touchPanel.touches[0].posY > 1f - cornerMargin)
                    touchThisFrame = true;
            }

            CornerHoldCheck(touchThisFrame);

        }
        else
        {
            CornerHoldCheck(false);
        }

        //        Debug.Log(hypercube.input.frontScreen.touches[0].posX + " " + hypercube.input.frontScreen.touches[0].posY);
    }

    void CornerHoldCheck(bool touch)
    {
        if (touch && !runningCoroutine)
        {
            timer += Time.deltaTime;
        }
        else if (!touch && runningCoroutine)
        {
            StopCoroutine("FadeInExitText");
            FindObjectOfType<hypercube.castMesh>().updateMesh();

            runningCoroutine = false;
            timer = 0f;
        }

        if (timer > beginOverlayTime && !runningCoroutine)
            StartCoroutine("FadeInExitText");
    }

    IEnumerator FadeInExitText()
    {
        runningCoroutine = true;
        for (int i = 0; i < exitTextPngs.Length; i++)
        {
            Material[] mats = hypercubeMesh.materials;
            mats[0].SetTexture("_MainTex", exitTextPngs[i]);
            mats[1].SetTexture("_MainTex", exitTextPngs[i]);

            yield return new WaitForSecondsRealtime(1f);

        }

        Application.Quit();

        System.Diagnostics.Process.Start(launcherPath);
    }
}