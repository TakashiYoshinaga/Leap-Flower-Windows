using UnityEngine;
using System.Collections;

public class FadeOut : MonoBehaviour {

	// Use this for initialization
	Material mat;
	public float fadeTime;
	public float waitTime;
	Light ligt;

	void Start () {
		ligt=GetComponentInChildren<Light>();

		mat=GetComponent<MeshRenderer>().sharedMaterial;

		mat.SetFloat("_Metallic",1f);
		StartCoroutine("FadeIn");

	}


	IEnumerator FadeIn(){
		float i =1f;
		while(i>=0f){
			ligt.intensity=i.Remap(0f,1f,3f,0f);
			mat.SetFloat("_Metallic",i);
			i-=Time.deltaTime/fadeTime;
			yield return null;
		}
		yield return new WaitForSeconds(waitTime);
		StartCoroutine("Fade");
	}

	IEnumerator Fade(){
		float i =0f;
		while(i<=1f){
			ligt.intensity=i.Remap(0f,1f,3f,0f);
			mat.SetFloat("_Metallic",i);
			i+=Time.deltaTime/fadeTime;
			yield return null;
		}
		Destroy(gameObject);
	}
}
