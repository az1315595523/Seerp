using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class LoadScene : MonoBehaviour
{
    public Image image1;
    public Image image2;
    public float fadeDuration = 1f; // 淡入淡出的持续时间

    private Coroutine fadeCoroutine;

    private void Start()
    {
        // 开始淡入淡出效果
        StartFadeInOut();
    }

    public void StartFadeInOut()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }

        fadeCoroutine = StartCoroutine(FadeInOut());
    }

    private IEnumerator FadeInOut()
    {
        // 将图像2完全透明
        image2.color = new Color(image2.color.r, image2.color.g, image2.color.b, 0f);

        // 淡入图像1
        yield return StartCoroutine(FadeImage(image1, 0f, 1f));

        // 淡出图像1
        yield return StartCoroutine(FadeImage(image1, 1f, 0f));

        // 淡入图像2
        yield return StartCoroutine(FadeImage(image2, 0f, 1f));

        // 淡出图像2
        yield return StartCoroutine(FadeImage(image2, 1f, 0f));
    }

    private IEnumerator FadeImage(Image image, float startAlpha, float endAlpha)
    {
        Color startColor = image.color;
        Color endColor = new Color(startColor.r, startColor.g, startColor.b, endAlpha);

        float elapsedTime = 0f;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / fadeDuration);
            image.color = Color.Lerp(startColor, endColor, t);
            yield return null;
        }

        image.color = endColor;
    }
    void Update()
    {
        
    }
}
