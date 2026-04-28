using System.Collections.Generic;
using Artect.Config;

namespace Artect.Generation.Emitters;

/// <summary>
/// V#5 PATCH support: emits the Optional&lt;T&gt; struct and its self-registering
/// JsonConverter into Shared/Common/. Only emitted when the user has Patch in
/// CRUD; otherwise no files are produced. Lives in Shared because the type is
/// part of the wire contract — both API consumers and the Application Patch
/// handler need to see it.
/// </summary>
public sealed class SharedCommonEmitter : IEmitter
{
    public IReadOnlyList<EmittedFile> Emit(EmitterContext ctx)
    {
        if ((ctx.Config.Crud & CrudOperation.Patch) == 0)
            return System.Array.Empty<EmittedFile>();

        var project = ctx.Config.ProjectName;
        var ns = $"{project}.Shared.Common";
        var dir = $"{CleanLayout.SharedDir(project)}/Common";

        return new[]
        {
            new EmittedFile($"{dir}/Optional.cs", BuildOptional(ns)),
            new EmittedFile($"{dir}/OptionalJsonConverter.cs", BuildJsonConverter(ns)),
        };
    }

    static string BuildOptional(string ns) => $$"""
        using System.Text.Json.Serialization;

        namespace {{ns}};

        /// <summary>
        /// Distinguishes three states for a PATCH request field: absent (HasValue=false),
        /// present with null (HasValue=true, Value=default), or present with a value
        /// (HasValue=true, Value=value). The handler uses HasValue to decide whether to
        /// apply the field. The [JsonConverter] attribute makes the type self-registering
        /// — no JsonSerializerOptions wiring needed.
        /// </summary>
        [JsonConverter(typeof(OptionalJsonConverterFactory))]
        public readonly struct Optional<T>
        {
            public bool HasValue { get; }
            public T? Value { get; }

            public Optional() { HasValue = false; Value = default; }
            public Optional(T? value) { HasValue = true; Value = value; }

            public static implicit operator Optional<T>(T value) => new(value);
        }
        """;

    static string BuildJsonConverter(string ns) => $$"""
        using System;
        using System.Text.Json;
        using System.Text.Json.Serialization;

        namespace {{ns}};

        /// <summary>
        /// Factory that produces a per-T <see cref="OptionalJsonConverter{T}"/> when
        /// System.Text.Json encounters an <see cref="Optional{T}"/> property. Wired via
        /// the [JsonConverter] attribute on Optional&lt;T&gt;; no global registration
        /// needed.
        /// </summary>
        public sealed class OptionalJsonConverterFactory : JsonConverterFactory
        {
            public override bool CanConvert(Type typeToConvert) =>
                typeToConvert.IsGenericType
                && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

            public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            {
                var t = typeToConvert.GetGenericArguments()[0];
                var converterType = typeof(OptionalJsonConverter<>).MakeGenericType(t);
                return (JsonConverter)Activator.CreateInstance(converterType)!;
            }
        }

        /// <summary>
        /// Per-T converter. The crucial behavior: when a JSON property is absent from the
        /// payload, System.Text.Json never invokes Read for that property — the default
        /// Optional&lt;T&gt; (HasValue=false) is used. When the property is present (null
        /// or value), Read produces an Optional with HasValue=true.
        /// </summary>
        public sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
        {
            public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var inner = JsonSerializer.Deserialize<T>(ref reader, options);
                return new Optional<T>(inner);
            }

            public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
            {
                if (!value.HasValue)
                {
                    writer.WriteNullValue();
                    return;
                }
                JsonSerializer.Serialize(writer, value.Value, options);
            }
        }
        """;
}
