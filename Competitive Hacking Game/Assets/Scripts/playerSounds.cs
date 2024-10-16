using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class playerSounds : MonoBehaviour {
    public List<AudioClip> walkSounds;
    public AudioSource audioSource;

    public int pos;

    public void stepSound() {
        pos = (int)Mathf.Floor(Random.Range(0, walkSounds.Count));
        audioSource.PlayOneShot(walkSounds[pos]);
    }
}
