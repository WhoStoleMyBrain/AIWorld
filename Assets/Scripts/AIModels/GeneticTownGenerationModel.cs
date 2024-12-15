using System.Collections.Generic;
using UnityEngine;

public class GeneticTownGenerationModel : ITownPositionBorderModel
{
    public Vector3 townPosition;
    public List<Vector3> townBorders;
    private float fitness;
    
    public GeneticTownGenerationModel()
    {
        // Initialize with random position and borders
        townPosition = new Vector3(Random.Range(-50f, 50f), 0, Random.Range(-50f, 50f));
        townBorders = GenerateBorders(townPosition);
    }

    // Implement the interface method for getting the town position
    public Vector3 GetTownPosition()
    {
        return townPosition;
    }

    // Implement the interface method for getting the town borders
    public List<Vector3> GetTownBorders()
    {
        return townBorders;
    }

    // Example of how you could generate borders based on the position
    private List<Vector3> GenerateBorders(Vector3 position)
    {
        // Simple logic: make a square border around the town's position
        List<Vector3> borders = new List<Vector3>();
        float size = 10f; // Just an example of size
        borders.Add(new Vector3(position.x - size, position.y, position.z - size)); // bottom-left
        borders.Add(new Vector3(position.x + size, position.y, position.z - size)); // bottom-right
        borders.Add(new Vector3(position.x + size, position.y, position.z + size)); // top-right
        borders.Add(new Vector3(position.x - size, position.y, position.z + size)); // top-left
        return borders;
    }

    // Fitness function (could be based on proximity to resources, etc.)
    public float CalculateFitness(Vector3 resourcePosition)
    {
        return 1 / (1 + Vector3.Distance(townPosition, resourcePosition)); // Closer to resource = better fitness
    }
}
