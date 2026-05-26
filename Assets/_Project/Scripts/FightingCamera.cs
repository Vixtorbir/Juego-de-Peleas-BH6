using UnityEngine;

public class FightingCamera : MonoBehaviour
{
    [Header("Luchadores")]
    public Transform player1;
    public Transform player2;

    [Header("Posicionamiento Base")]
    public Vector3 baseOffset = new Vector3(0f, 1.5f, 0f); 
    public float pitchAngle = 8f;

    [Header("Sistema de Zoom Dinámico")]
    public float zoomMultiplier = 0.4f;  
    public float minZoomDistance = -6f;  
    public float maxZoomDistance = -14f; 

    [Header("Límites del Escenario (Esquinas)")]
    public float leftBound = -8f; 
    public float rightBound = 8f; 

    [Header("Sensación y Fluidez")]
    public float smoothTime = 0.15f;
    private Vector3 currentVelocity;

    [Header("Screen Shake")]
    private float shakeTimer;
    private float currentShakeMagnitude;

    private void Start()
    {
        transform.rotation = Quaternion.Euler(pitchAngle, 0f, 0f);
    }

    public void ShakeCamera(float duration, float magnitude)
    {
        shakeTimer = duration;
        currentShakeMagnitude = magnitude;
    }
    private void LateUpdate()
    {
        if (player1 == null || player2 == null) return;

        // 1. Calcular el PUNTO MEDIO entre ambos luchadores
        Vector3 midPoint = (player1.position + player2.position) / 2f;

        // 2. Calcular la DISTANCIA ABSOLUTA entre ellos
        float distanceBetweenPlayers = Vector3.Distance(player1.position, player2.position);

        // 3. ZOOM DINÁMICO: Modificamos la profundidad (Z) basándonos en la distancia
        float calculatedZ = baseOffset.z - (distanceBetweenPlayers * zoomMultiplier);

        calculatedZ = Mathf.Clamp(calculatedZ, maxZoomDistance, minZoomDistance);

        // 4. CONTROL DE ESQUINAS: Evitamos que el punto medio arrastre la cámara fuera del mapa
        float clampedX = Mathf.Clamp(midPoint.x + baseOffset.x, leftBound, rightBound);

        // 5. Construir la posición final donde la cámara DEBERÍA estar este fotograma
        Vector3 targetPosition = new Vector3(clampedX, midPoint.y + baseOffset.y, calculatedZ);

        // 6. MOVIMIENTO FLUIDO: Interpolamos la posición actual hacia la deseada
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, smoothTime);

        // 7. APLICAR SCREEN SHAKE (Se aplica al final del todo usando tiempo no escalado)
        if (shakeTimer > 0f)
        {
            transform.position += Random.insideUnitSphere * currentShakeMagnitude;

            shakeTimer -= Time.unscaledDeltaTime;
        }
    }

}