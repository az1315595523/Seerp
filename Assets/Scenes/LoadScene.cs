using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class LoadScene : MonoBehaviour
{
    public Image tip1Image;
    public Image tip2Image;
    public float fadeDuration = 1f;
    public float delayBetweenTips = 2f;

    private void Start()
    {
        // 开始循环渐变效果

        tip1Image.color = new Color(tip1Image.color.r, tip1Image.color.g, tip1Image.color.b, 0f);
        tip2Image.color = new Color(tip2Image.color.r, tip2Image.color.g, tip2Image.color.b, 0f);

        StartCoroutine(AnimateTips());
    }

    private System.Collections.IEnumerator AnimateTips()
    {
        while (true)
        {
            // Tip1 登入
            yield return FadeIn(tip1Image);
            yield return new WaitForSeconds(delayBetweenTips);

            // Tip1 登出
            yield return FadeOut(tip1Image);

            // Tip2 登入
            yield return FadeIn(tip2Image);
            yield return new WaitForSeconds(delayBetweenTips);

            // Tip2 登出
            yield return FadeOut(tip2Image);
        }
    }

    private System.Collections.IEnumerator FadeIn(Image image)
    {
        float timer = 0f;
        Color startColor = image.color;
        Color targetColor = new Color(startColor.r, startColor.g, startColor.b, 1f);
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float alpha = Mathf.Lerp(startColor.a, targetColor.a, timer / fadeDuration);
            image.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            yield return null;
        }
        image.color = targetColor;
    }

    private System.Collections.IEnumerator FadeOut(Image image)
    {
        float timer = 0f;
        Color startColor = image.color;
        Color targetColor = new Color(startColor.r, startColor.g, startColor.b, 0f);
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float alpha = Mathf.Lerp(startColor.a, targetColor.a, timer / fadeDuration);
            image.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            yield return null;
        }
        image.color = targetColor;
    }
    void Update()
    {
        
    }
}
