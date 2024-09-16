using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using System;
using System.Linq;

namespace AngleSharpExperiments.AngleSharpRendering;

internal static class AngleSharpExtensions
{
    private const string DeferredValuePropName = "_blazorDeferredValue";

    // Updating the attributes/properties on DOM elements involves a whole range of special cases, because
    // depending on the element type, there are special rules for needing to update other properties or
    // to only perform the changes in a specific order.
    //
    // This module provides helpers for doing that, and is shared by the interactive renderer (AngleSharpRenderer)
    // and the SSR DOM merging logic.
    public static bool TryApplySpecialProperty(this IElement element, string name, string? value)
    {
        switch (name)
        {
            case "value":
                return element.TryApplyValueProperty(value);
            case "checked":
                return element.TryApplyCheckedProperty(value);
            default:
                return false;
        }
    }

    public static void ApplyAnyDeferredValue(this IElement element)
    {
        // We handle setting 'value' on a <select> in three different ways:
        // [1] When inserting a corresponding <option>, in case you're dynamically adding options.
        //     This is the case below.
        // [2] After we finish inserting the <select>, in case the descendant options are being
        //     added as an opaque markup block rather than individually. This is the other case below.
        // [3] In case the the value of the select and the option value is changed in the same batch.
        //     We just receive an attribute frame and have to set the select value afterwards.

        // We also defer setting the 'value' property for <input> because certain types of inputs have
        // default attribute values that may incorrectly constain the specified 'value'.
        // For example, range inputs have default 'min' and 'max' attributes that may incorrectly
        // clamp the 'value' property if it is applied before custom 'min' and 'max' attributes.

        if (element is IHtmlOptionElement optionElement)
        {
            // Situation 1
            optionElement.TrySetSelectValueFromOptionElement();
        }
        else if (element.HasAttribute(DeferredValuePropName))
        {
            // Situation 2
            var deferredValue = element.GetAttribute(DeferredValuePropName);
            element.SetDeferredElementValue(deferredValue);
        }
    }

    private static bool TryApplyCheckedProperty(this IElement element, string? value)
    {
        // Certain elements have built-in behaviour for their 'checked' property
        if (element is IHtmlInputElement inputElement)
        {
            inputElement.IsChecked = value is not null;
            return true;
        }

        return false;
    }

    private static bool TryApplyValueProperty(this IElement element, string? value)
    {
        // Certain elements have built-in behaviour for their 'value' property
        if (value != null && element is IHtmlInputElement inputElement)
        {
            value = inputElement.NormalizeInputValue(value);
        }

        switch (element)
        {
            case IHtmlInputElement _:
            case IHtmlSelectElement _:
            case IHtmlTextAreaElement _:
                // <select> is special, in that anything we write to .value will be lost if there
                // isn't yet a matching <option>. To maintain the expected behavior no matter the
                // element insertion/update order, preserve the desired value separately so
                // we can recover it when inserting any matching <option> or after inserting an
                // entire markup block of descendants.

                // We also defer setting the 'value' property for <input> because certain types of inputs have
                // default attribute values that may incorrectly constain the specified 'value'.
                // For example, range inputs have default 'min' and 'max' attributes that may incorrectly
                // clamp the 'value' property if it is applied before custom 'min' and 'max' attributes.

                if (value != null && element is IHtmlSelectElement selectElement && selectElement.IsMultiple)
                {
                    value = System.Text.Json.JsonSerializer.Serialize(value);
                }

                element.SetDeferredElementValue(value);
                element.SetAttribute(DeferredValuePropName, value);
                return true;

            case IHtmlOptionElement optionElement:
                if (value is { Length: 0 })
                {
                    optionElement.SetAttribute("value", value);
                }
                else
                {
                    optionElement.RemoveAttribute("value");
                }

                // See above for why we have this special handling for <select>/<option>
                // Situation 3
                optionElement.TrySetSelectValueFromOptionElement();
                return true;

            default:
                return false;
        }
    }

    private static string NormalizeInputValue(this IElement element, string value)
    {
        // Time inputs (e.g. 'time' and 'datetime-local') misbehave on chromium-based
        // browsers when a time is set that includes a seconds value of '00', most notably
        // when entered from keyboard input. This behavior is not limited to specific
        // 'step' attribute values, so we always remove the trailing seconds value if the
        // time ends in '00'.
        // Similarly, if a time-related element doesn't have any 'step' attribute, browsers
        // treat this as "round to whole number of minutes" making it invalid to pass any
        // 'seconds' value, so in that case we strip off the 'seconds' part of the value.

        var type = element.GetAttribute("type");
        switch (type)
        {
            case "time":
                return value.Length == 8 && (value.EndsWith("00") || !element.HasAttribute("step"))
                    ? value.Substring(0, 5)
                    : value;
            case "datetime-local":
                return value.Length == 19 && (value.EndsWith("00") || !element.HasAttribute("step"))
                    ? value.Substring(0, 16)
                    : value;
            default:
                return value;
        }
    }

    private static void SetDeferredElementValue(this IElement element, string? value)
    {
        if (element is IHtmlSelectElement selectElement)
        {
            if (selectElement.IsMultiple)
            {
                SetMultipleSelectElementValue(selectElement, value);
            }
            else
            {
                SetSingleSelectElementValue(selectElement, value);
            }
        }
        else
        {
            element.SetAttribute("value", value);
        }

        element.RemoveAttribute(DeferredValuePropName);
    }

    private static void SetSingleSelectElementValue(IHtmlSelectElement element, string value)
    {
        // There's no sensible way to represent a select option with value 'null', because
        // (1) HTML attributes can't have null values - the closest equivalent is absence of the attribute
        // (2) When picking an <option> with no 'value' attribute, the browser treats the value as being the
        //     *text content* on that <option> element. Trying to suppress that default behavior would involve
        //     a long chain of special-case hacks, as well as being breaking vs 3.x.
        // So, the most plausible 'null' equivalent is an empty string. It's unfortunate that people can't
        // write <option value=@someNullVariable>, and that we can never distinguish between null and empty
        // string in a bound <select>, but that's a limit in the representational power of HTML.
        element.Value = value ?? string.Empty;
    }

    private static void SetMultipleSelectElementValue(IHtmlSelectElement element, string? value)
    {
        var values = System.Text.Json.JsonSerializer.Deserialize<string[]>(value) ?? Array.Empty<string>();
        foreach (var option in element.Options)
        {
            option.IsSelected = values.Contains(option.Value);
        }
    }

    private static void TrySetSelectValueFromOptionElement(this IHtmlOptionElement optionElement)
    {
        var selectElement = optionElement.FindClosestAncestorSelectElement();
        if (selectElement is null || !selectElement.HasAttribute(DeferredValuePropName))
        {
            return;
        }

        if (selectElement.IsMultiple)
        {
            optionElement.IsSelected = selectElement.GetAttribute(DeferredValuePropName)?.Contains(optionElement.Value) ?? false;
        }
        else
        {
            if (selectElement.GetAttribute(DeferredValuePropName) != optionElement.Value)
            {
                return;
            }

            SetSingleSelectElementValue(selectElement, optionElement.Value);
            selectElement.RemoveAttribute(DeferredValuePropName);
        }
    }

    private static IHtmlSelectElement? FindClosestAncestorSelectElement(this IElement element)
    {
        IElement? candidate = element;
        while (candidate is not null)
        {
            if (candidate is IHtmlSelectElement selectElement)
            {
                return selectElement;
            }

            candidate = element.ParentElement;
        }

        return null;
    }
}
