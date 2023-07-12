using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class StartButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,IPointerDownHandler, IPointerUpHandler
{
    public Image circleLight; // ������Ҫ��ʾ�����ص�Image����

    public Sprite hover;

    public Sprite pressed;

    public Sprite normal;

    public Button button;

    public Image image;

    public float rotationSpeed = 100f; // ������ת�ٶ�

    public bool isHovering;


    // ��Start�����л�ȡImage��������ò�������
    void Start()
    {

        circleLight.gameObject.SetActive(false);
        image = button.GetComponent<Image>();
    }

    // ��Update������ʵʱ�������Ƿ���ͣ�ڰ�ť�ϣ������������ʾ������Image����
    void Update()
    {
        if(isHovering)
        {
            if (!circleLight.gameObject.activeSelf)
                circleLight.gameObject.SetActive(true);
            // ��תImage����
            circleLight.transform.Rotate(Vector3.forward * rotationSpeed * Time.deltaTime);
        }
        else
        {
            if (circleLight.gameObject.activeSelf)
                circleLight.gameObject.SetActive(false);
        }
    }

    // �����밴ť����ʱ���øú���
    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        image.sprite = hover;
    }

    // ����뿪��ť����ʱ���øú���
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
