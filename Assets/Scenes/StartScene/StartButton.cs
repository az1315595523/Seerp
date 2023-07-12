using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class StartButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,IPointerDownHandler, IPointerUpHandler
{
    public Image circleLight; // 引用需要显示和隐藏的Image对象

    public Sprite hover;

    public Sprite pressed;

    public Sprite normal;

    public Button button;

    public Image image;

    public float rotationSpeed = 100f; // 设置旋转速度

    public bool isHovering;


    // 在Start函数中获取Image组件的引用并隐藏它
    void Start()
    {

        circleLight.gameObject.SetActive(false);
        image = button.GetComponent<Image>();
    }

    // 在Update函数中实时检测鼠标是否悬停在按钮上，并根据情况显示或隐藏Image对象
    void Update()
    {
        if(isHovering)
        {
            if (!circleLight.gameObject.activeSelf)
                circleLight.gameObject.SetActive(true);
            // 旋转Image对象
            circleLight.transform.Rotate(Vector3.forward * rotationSpeed * Time.deltaTime);
        }
        else
        {
            if (circleLight.gameObject.activeSelf)
                circleLight.gameObject.SetActive(false);
        }
    }

    // 鼠标进入按钮区域时调用该函数
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        image.sprite = hover;
    }

    // 鼠标离开按钮区域时调用该函数
    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        image.sprite = normal;
    }
    public void OnPointerDown(PointerEventData eventData)
    {
        image.sprite = pressed; 
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        image.sprite = hover;
    }
}
