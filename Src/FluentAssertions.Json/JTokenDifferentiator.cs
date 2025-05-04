﻿using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions.Equivalency;
using FluentAssertions.Equivalency.Tracing;
using FluentAssertions.Execution;
using Newtonsoft.Json.Linq;

namespace FluentAssertions.Json
{
    internal class JTokenDifferentiator
    {
        private readonly bool ignoreExtraProperties;
        private readonly JValueEquivalencyValidator equivalencyValidator;

        public JTokenDifferentiator(bool ignoreExtraProperties,
            Func<IJsonAssertionOptions<object>, IJsonAssertionOptions<object>> config)
        {
            this.ignoreExtraProperties = ignoreExtraProperties;
            // Let's cache an options instance, so we don't create a new one for each invocation of BeEquivalentTo
            var options = (JsonAssertionOptions<object>)config.Invoke(new JsonAssertionOptions<object>(AssertionConfiguration.Current.Equivalency.CloneDefaults<object>()));
            this.equivalencyValidator = new JValueEquivalencyValidator(options);
        }

        public Difference FindFirstDifference(JToken actual, JToken expected)
        {
            var path = new JPath();

            if (actual == expected)
            {
                return null;
            }

            if (actual == null)
            {
                return new Difference(DifferenceKind.ActualIsNull, path);
            }

            if (expected == null)
            {
                return new Difference(DifferenceKind.ExpectedIsNull, path);
            }

            return FindFirstDifference(actual, expected, path);
        }

        private Difference FindFirstDifference(JToken actual, JToken expected, JPath path)
        {
            return actual switch
            {
                JArray actualArray => FindJArrayDifference(actualArray, expected, path),
                JObject actualObject => FindJObjectDifference(actualObject, expected, path),
                JProperty actualProperty => FindJPropertyDifference(actualProperty, expected, path),
                JValue actualValue => FindValueDifference(actualValue, expected, path),
                _ => throw new NotSupportedException(),
            };
        }

        private Difference FindJArrayDifference(JArray actualArray, JToken expected, JPath path)
        {
            if (expected is not JArray expectedArray)
            {
                return new Difference(DifferenceKind.OtherType, path, Describe(actualArray.Type), Describe(expected.Type));
            }

            if (ignoreExtraProperties)
            {
                return CompareExpectedItems(actualArray, expectedArray, path);
            }
            else
            {
                return CompareItems(actualArray, expectedArray, path);
            }
        }

        private Difference CompareExpectedItems(JArray actual, JArray expected, JPath path)
        {
            JToken[] actualChildren = actual.Children().ToArray();
            JToken[] expectedChildren = expected.Children().ToArray();

            int matchingIndex = 0;
            for (int expectedIndex = 0; expectedIndex < expectedChildren.Length; expectedIndex++)
            {
                var expectedChild = expectedChildren[expectedIndex];
                bool match = false;
                for (int actualIndex = matchingIndex; actualIndex < actualChildren.Length; actualIndex++)
                {
                    var difference = FindFirstDifference(actualChildren[actualIndex], expectedChild);

                    if (difference == null)
                    {
                        match = true;
                        matchingIndex = actualIndex + 1;
                        break;
                    }
                }

                if (!match)
                {
                    if (matchingIndex >= actualChildren.Length)
                    {
                        if (actualChildren.Any(actualChild => FindFirstDifference(actualChild, expectedChild) == null))
                        {
                            return new Difference(DifferenceKind.WrongOrder, path.AddIndex(expectedIndex));
                        }

                        return new Difference(DifferenceKind.ActualMissesElement, path.AddIndex(expectedIndex));
                    }

                    return FindFirstDifference(actualChildren[matchingIndex], expectedChild,
                        path.AddIndex(expectedIndex));
                }
            }

            return null;
        }

        private Difference CompareItems(JArray actual, JArray expected, JPath path)
        {
            JToken[] actualChildren = actual.Children().ToArray();
            JToken[] expectedChildren = expected.Children().ToArray();

            if (actualChildren.Length != expectedChildren.Length)
            {
                return new Difference(DifferenceKind.DifferentLength, path, actualChildren.Length, expectedChildren.Length);
            }

            for (int i = 0; i < actualChildren.Length; i++)
            {
                Difference firstDifference = FindFirstDifference(actualChildren[i], expectedChildren[i], path.AddIndex(i));

                if (firstDifference != null)
                {
                    return firstDifference;
                }
            }

            return null;
        }

        private Difference FindJObjectDifference(JObject actual, JToken expected, JPath path)
        {
            if (expected is not JObject expectedObject)
            {
                return new Difference(DifferenceKind.OtherType, path, Describe(actual.Type), Describe(expected.Type));
            }

            return CompareProperties(actual?.Properties(), expectedObject.Properties(), path);
        }

        private Difference CompareProperties(IEnumerable<JProperty> actual, IEnumerable<JProperty> expected, JPath path)
        {
            var actualDictionary = actual?.ToDictionary(p => p.Name, p => p.Value) ?? new Dictionary<string, JToken>();
            var expectedDictionary = expected?.ToDictionary(p => p.Name, p => p.Value) ?? new Dictionary<string, JToken>();

            foreach (KeyValuePair<string, JToken> expectedPair in expectedDictionary)
            {
                if (!actualDictionary.ContainsKey(expectedPair.Key))
                {
                    return new Difference(DifferenceKind.ActualMissesProperty, path.AddProperty(expectedPair.Key));
                }
            }

            foreach (KeyValuePair<string, JToken> actualPair in actualDictionary)
            {
                if (!ignoreExtraProperties && !expectedDictionary.ContainsKey(actualPair.Key))
                {
                    return new Difference(DifferenceKind.ExpectedMissesProperty, path.AddProperty(actualPair.Key));
                }
            }

            foreach (KeyValuePair<string, JToken> expectedPair in expectedDictionary)
            {
                JToken actualValue = actualDictionary[expectedPair.Key];

                Difference firstDifference = FindFirstDifference(actualValue, expectedPair.Value,
                    path.AddProperty(expectedPair.Key));

                if (firstDifference != null)
                {
                    return firstDifference;
                }
            }

            return null;
        }

        private Difference FindJPropertyDifference(JProperty actualProperty, JToken expected, JPath path)
        {
            if (expected is not JProperty expectedProperty)
            {
                return new Difference(DifferenceKind.OtherType, path, Describe(actualProperty.Type), Describe(expected.Type));
            }

            if (actualProperty.Name != expectedProperty.Name)
            {
                return new Difference(DifferenceKind.OtherName, path);
            }

            return FindFirstDifference(actualProperty.Value, expectedProperty.Value, path);
        }

        private Difference FindValueDifference(JValue actualValue, JToken expected, JPath path)
        {
            if (expected is not JValue expectedValue)
            {
                return new Difference(DifferenceKind.OtherType, path, Describe(actualValue.Type), Describe(expected.Type));
            }

            return CompareValues(actualValue, expectedValue, path);
        }

        private Difference CompareValues(JValue actual, JValue expected, JPath path)
        {
            if (actual.Type != expected.Type)
            {
                return new Difference(DifferenceKind.OtherType, path, Describe(actual.Type), Describe(expected.Type));
            }

            using var scope = new AssertionScope();
            equivalencyValidator.AssertEquality(actual, expected);
            if (scope.Discard().Length > 0)
            {
                return new Difference(DifferenceKind.OtherValue, path);
            }
            return null;
        }

        private static string Describe(JTokenType jTokenType)
        {
            return jTokenType switch
            {
                JTokenType.None => "type none",
                JTokenType.Object => "an object",
                JTokenType.Array => "an array",
                JTokenType.Constructor => "a constructor",
                JTokenType.Property => "a property",
                JTokenType.Comment => "a comment",
                JTokenType.Integer => "an integer",
                JTokenType.Float => "a float",
                JTokenType.String => "a string",
                JTokenType.Boolean => "a boolean",
                JTokenType.Null => "type null",
                JTokenType.Undefined => "type undefined",
                JTokenType.Date => "a date",
                JTokenType.Raw => "type raw",
                JTokenType.Bytes => "type bytes",
                JTokenType.Guid => "a GUID",
                JTokenType.Uri => "a URI",
                JTokenType.TimeSpan => "a timespan",
                _ => throw new ArgumentOutOfRangeException(nameof(jTokenType), jTokenType, null),
            };
        }
    }

    internal class Difference
    {
        public Difference(DifferenceKind kind, JPath path, object actual, object expected)
            : this(kind, path)
        {
            Actual = actual;
            Expected = expected;
        }

        public Difference(DifferenceKind kind, JPath path)
        {
            Kind = kind;
            Path = path;
        }

        private DifferenceKind Kind { get; }

        private JPath Path { get; }

        private object Actual { get; }

        private object Expected { get; }

        public override string ToString()
        {
            return Kind switch
            {
                DifferenceKind.ActualIsNull => "is null",
                DifferenceKind.ExpectedIsNull => "is not null",
                DifferenceKind.OtherType => $"has {Actual} instead of {Expected} at {Path}",
                DifferenceKind.OtherName => $"has a different name at {Path}",
                DifferenceKind.OtherValue => $"has a different value at {Path}",
                DifferenceKind.DifferentLength => $"has {Actual} elements instead of {Expected} at {Path}",
                DifferenceKind.ActualMissesProperty => $"misses property {Path}",
                DifferenceKind.ExpectedMissesProperty => $"has extra property {Path}",
                DifferenceKind.ActualMissesElement => $"misses expected element {Path}",
                DifferenceKind.WrongOrder => $"has expected element {Path} in the wrong order",
#pragma warning disable S3928 // Parameter names used into ArgumentException constructors should match an existing one 
                _ => throw new ArgumentOutOfRangeException(),
#pragma warning restore S3928 // Parameter names used into ArgumentException constructors should match an existing one 
            };
        }
    }

    internal class JPath
    {
        private readonly List<string> nodes = new();

        public JPath()
        {
            nodes.Add("$");
        }

        private JPath(JPath existingPath, string extraNode)
        {
            nodes.AddRange(existingPath.nodes);
            nodes.Add(extraNode);
        }

        public JPath AddProperty(string name)
        {
            return new JPath(this, $".{name}");
        }

        public JPath AddIndex(int index)
        {
            return new JPath(this, $"[{index}]");
        }

        public override string ToString()
        {
            return string.Concat(nodes);
        }
    }

    internal enum DifferenceKind
    {
        ActualIsNull,
        ExpectedIsNull,
        OtherType,
        OtherName,
        OtherValue,
        DifferentLength,
        ActualMissesProperty,
        ExpectedMissesProperty,
        ActualMissesElement,
        WrongOrder
    }
}
