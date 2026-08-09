using System.Text.Json;
using System.Text.Json.Serialization;
using Clipora.Core.Models;

namespace Clipora.Core.Services;

internal sealed class TrimShortcutBindingsJsonConverter : JsonConverter<TrimShortcutBindings>
{
    public override bool HandleNull => true;

    public override TrimShortcutBindings Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return TrimShortcutBindings.Default;
        }

        if (reader.TokenType is not JsonTokenType.StartObject)
        {
            SkipNestedValue(ref reader);
            return TrimShortcutBindings.Default;
        }

        TrimShortcutBindings bindings = TrimShortcutBindings.Default;
        bool invalid = false;
        bool playPauseSeen = false;
        bool setStartSeen = false;
        bool setEndSeen = false;
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndObject)
            {
                return invalid ? TrimShortcutBindings.Default : bindings;
            }

            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                invalid = true;
                reader.Skip();
                continue;
            }

            string propertyName = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of shortcut settings.");
            }

            switch (propertyName.ToUpperInvariant())
            {
                case "PLAYPAUSE":
                    if (playPauseSeen)
                    {
                        invalid = true;
                        SkipNestedValue(ref reader);
                    }
                    else if (!TryReadGesture(ref reader, out ShortcutGesture playPause))
                    {
                        invalid = true;
                    }
                    else
                    {
                        bindings = bindings with { PlayPause = playPause };
                    }

                    playPauseSeen = true;
                    break;
                case "SETSTART":
                    if (setStartSeen)
                    {
                        invalid = true;
                        SkipNestedValue(ref reader);
                    }
                    else if (!TryReadGesture(ref reader, out ShortcutGesture setStart))
                    {
                        invalid = true;
                    }
                    else
                    {
                        bindings = bindings with { SetStart = setStart };
                    }

                    setStartSeen = true;
                    break;
                case "SETEND":
                    if (setEndSeen)
                    {
                        invalid = true;
                        SkipNestedValue(ref reader);
                    }
                    else if (!TryReadGesture(ref reader, out ShortcutGesture setEnd))
                    {
                        invalid = true;
                    }
                    else
                    {
                        bindings = bindings with { SetEnd = setEnd };
                    }

                    setEndSeen = true;
                    break;
                default:
                    invalid = true;
                    SkipNestedValue(ref reader);
                    break;
            }
        }

        throw new JsonException("Unexpected end of shortcut settings.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        TrimShortcutBindings value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        WriteGesture(writer, "playPause", value.PlayPause);
        WriteGesture(writer, "setStart", value.SetStart);
        WriteGesture(writer, "setEnd", value.SetEnd);
        writer.WriteEndObject();
    }

    private static bool TryReadGesture(
        ref Utf8JsonReader reader,
        out ShortcutGesture gesture)
    {
        gesture = default;
        if (reader.TokenType is not JsonTokenType.StartObject)
        {
            SkipNestedValue(ref reader);
            return false;
        }

        ShortcutKey key = ShortcutKey.None;
        ShortcutModifiers modifiers = ShortcutModifiers.None;
        bool keySeen = false;
        bool modifiersSeen = false;
        bool invalid = false;
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.EndObject)
            {
                if (!keySeen || invalid)
                {
                    return false;
                }

                gesture = new ShortcutGesture(key, modifiers);
                return true;
            }

            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                invalid = true;
                reader.Skip();
                continue;
            }

            string propertyName = reader.GetString() ?? string.Empty;
            if (!reader.Read())
            {
                throw new JsonException("Unexpected end of shortcut gesture.");
            }

            switch (propertyName.ToUpperInvariant())
            {
                case "KEY":
                    if (keySeen)
                    {
                        invalid = true;
                        SkipNestedValue(ref reader);
                    }
                    else if (reader.TokenType is not JsonTokenType.String ||
                        !ShortcutGesture.TryParse(reader.GetString(), out ShortcutGesture parsedKey) ||
                        parsedKey.Modifiers is not ShortcutModifiers.None)
                    {
                        invalid = true;
                        SkipNestedValue(ref reader);
                    }
                    else
                    {
                        key = parsedKey.Key;
                    }

                    keySeen = true;
                    break;
                case "MODIFIERS":
                    if (modifiersSeen)
                    {
                        invalid = true;
                        SkipNestedValue(ref reader);
                    }
                    else if (reader.TokenType is not JsonTokenType.String ||
                        !Enum.TryParse(
                            reader.GetString(),
                            ignoreCase: true,
                            out modifiers))
                    {
                        invalid = true;
                        SkipNestedValue(ref reader);
                    }

                    modifiersSeen = true;
                    break;
                default:
                    invalid = true;
                    SkipNestedValue(ref reader);
                    break;
            }
        }

        throw new JsonException("Unexpected end of shortcut gesture.");
    }

    private static void SkipNestedValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            reader.Skip();
        }
    }

    private static void WriteGesture(
        Utf8JsonWriter writer,
        string propertyName,
        ShortcutGesture gesture)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartObject();
        writer.WriteString("key", gesture.Key.ToString());
        writer.WriteString("modifiers", gesture.Modifiers.ToString());
        writer.WriteEndObject();
    }
}
