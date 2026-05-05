using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MovieRatingAgent.Agent.Declarative;

/// <summary>
/// POCO mirror of a scorer agent's YAML file. The YAML format is a compatible
/// subset of Microsoft Agent Framework's declarative agent shape — we can swap
/// in <c>Microsoft.Agents.AI.Declarative.ChatClientPromptAgentFactory</c> against
/// the same files when that package ships GA.
/// </summary>
public sealed class ScorerAgentDefinition
{
    public string? Kind { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string Instructions { get; set; } = "";
    public ModelSection? Model { get; set; }

    /// <summary>
    /// JSON Schema describing the structured output. Currently informational —
    /// the actual response schema is enforced by the C# <c>ScorerLlmOutput</c>
    /// record. When the MAF declarative package goes GA, this becomes the
    /// authoritative source.
    /// </summary>
    public Dictionary<string, object?>? OutputSchema { get; set; }

    public sealed class ModelSection
    {
        public string? Id { get; set; }
        public ModelOptionsSection? Options { get; set; }
    }

    public sealed class ModelOptionsSection
    {
        public float? Temperature { get; set; }
        public float? TopP { get; set; }
    }

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ScorerAgentDefinition LoadFromFile(string path)
    {
        var yaml = File.ReadAllText(path);
        return Parse(yaml);
    }

    public static ScorerAgentDefinition Parse(string yaml)
    {
        var def = Deserializer.Deserialize<ScorerAgentDefinition>(yaml)
            ?? throw new InvalidOperationException("YAML deserialized to null.");

        if (string.IsNullOrWhiteSpace(def.Name))
            throw new InvalidOperationException("Scorer YAML is missing required 'name' field.");
        if (string.IsNullOrWhiteSpace(def.Instructions))
            throw new InvalidOperationException($"Scorer YAML '{def.Name}' is missing required 'instructions' field.");

        return def;
    }
}
