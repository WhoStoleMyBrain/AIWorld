using System.Collections.Generic;
using UnityEngine;

// Define an interface that all town generation models must implement

public interface ITownPositionBorderModel
{
    // Method to get the position of the town (e.g., Vector3)
    Vector3 GetTownPosition();

    // Method to get the borders of the town (e.g., a List of Vector3 points defining the border)
    List<Vector3> GetTownBorders();
}
