using KartGame.KartSystems;
using UnityEngine;

public static class PlayerVehicleUtility
{
    public static bool IsPlayerVehicle(Collider other)
    {
        if (other == null)
            return false;

        var keyboardInput = other.GetComponentInParent<KeyboardInput>();
        return keyboardInput != null && keyboardInput.isActiveAndEnabled;
    }
}
