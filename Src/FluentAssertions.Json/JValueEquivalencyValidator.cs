using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions.Equivalency;
using FluentAssertions.Equivalency.Tracing;
using FluentAssertions.Execution;
using Newtonsoft.Json.Linq;

namespace FluentAssertions.Json
{
    /// <summary>
    /// Copy of the relevant parts of EquivalencyValidator for comparing the values of JValue objects.
    /// We only need the loop over AssertionConfiguration.Current.Equivalency.Plan as the JValue is never a collection.
    /// </summary>
    internal class JValueEquivalencyValidator : IValidateChildNodeEquivalency
    {
        private readonly JValueContext cachedRootContext;
        public JValueEquivalencyValidator(IEquivalencyOptions options)
        {
            cachedRootContext = new JValueContext(options);
        }

        public void AssertEquality(JValue actual, JValue expected)
        {
            var comparands = new Comparands(actual.Value, expected.Value, typeof(object));
            AssertEquivalencyOf(comparands, cachedRootContext.Clone());
        }

        public void AssertEquivalencyOf(Comparands comparands, IEquivalencyValidationContext context)
        {
            TryToProveNodesAreEquivalent(comparands, context);
        }

        private void TryToProveNodesAreEquivalent(Comparands comparands, IEquivalencyValidationContext context)
        {
            foreach (IEquivalencyStep step in AssertionConfiguration.Current.Equivalency.Plan)
            {
                var result = step.Handle(comparands, context, this);

                if (result == EquivalencyResult.EquivalencyProven)
                {
                    return;
                }
            }
            throw new NotSupportedException(
                $"Do not know how to compare {comparands.Subject} and {comparands.Expectation}. Please report an issue through https://www.fluentassertions.com.");
        }

        private class JValueContext : IEquivalencyValidationContext
        {
            public INode CurrentNode { get; }
            public Reason Reason => new(string.Empty, Array.Empty<string>());
            public Tracer Tracer { get; }
            public IEquivalencyOptions Options { get; }

            public JValueContext(IEquivalencyOptions options)
            {
                Options = options;
                CurrentNode = ReflectionHelpers.CreateRootNode();
                Tracer = ReflectionHelpers.CreateEmptyTracer(CurrentNode);
            }

            public JValueContext(IEquivalencyOptions options, INode currentNode, Tracer tracer) : this(options)
            {
                Options = options;
                CurrentNode = currentNode;
                Tracer = tracer;
            }

            public IEquivalencyValidationContext AsCollectionItem<TItem>(string index) => throw new NotImplementedException();
            public IEquivalencyValidationContext AsDictionaryItem<TKey, TExpectation>(TKey key) => throw new NotImplementedException();
            public IEquivalencyValidationContext AsNestedMember(IMember expectationMember) => throw new NotImplementedException();
            public IEquivalencyValidationContext Clone() => new JValueContext(Options, CurrentNode, Tracer);
            public bool IsCyclicReference(object expectation) => false;
        }

        /// <summary>
        /// Helpers to access implementations that are not possible to implement external to FluentAssertions library.
        /// Node cause INode cannot be implemented cause of an internal setter.
        /// Tracer cause it has an internal constructor and is needed for the IEquivalencyValidationContext as some validation expect it to not be null.
        /// </summary>
        private static class ReflectionHelpers
        {
            private static readonly Type nodeType;
            private static readonly PropertyInfo nodeSubjectProperty;
            private static readonly ConstructorInfo tracerConstructor;

            static ReflectionHelpers()
            {
                nodeType = typeof(INode).Assembly.GetTypes().Single(t => t.FullName == "FluentAssertions.Equivalency.Node");
                nodeSubjectProperty = nodeType.GetProperty(nameof(INode.Subject));
                tracerConstructor = typeof(Tracer).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, CallingConventions.HasThis, new[] { typeof(INode), typeof(ITraceWriter) }, null);
            }

            /// <summary>
            /// Create a root Node.
            /// </summary>
            /// <remarks>Needs reflection cause cause INode cannot be implemented cause of an internal setter.</remarks>
            public static INode CreateRootNode()
            {
                var node = (INode)Activator.CreateInstance(nodeType);
                nodeSubjectProperty.SetValue(node, new Pathway(string.Empty, string.Empty, _ => "root"));
                return node;
            }

            /// <summary>
            /// Create a tracer that does nothing.
            /// </summary>
            /// <param name="node">The not to point to</param>
            /// <remarks>Needed cause it has an internal constructor and is needed for the IEquivalencyValidationContext as some validation expect it to not be null</remarks>
            public static Tracer CreateEmptyTracer(INode node)
            {
                return (Tracer)tracerConstructor.Invoke(new[] { node, null });
            }
        }
    }
}
