namespace Models.Autorization.Attributes;

/// <summary>Алиас для [Tool(Public = true)].</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class PublicAttribute : Attribute { }