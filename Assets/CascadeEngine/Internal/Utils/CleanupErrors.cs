#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace CascadeEngineApi
{
    /// <summary>
    /// Collects cleanup failures without allocating on successful cleanup. Owners finish releasing storage before throwing.
    /// </summary>
    internal struct CleanupErrors
    {
        private List<Exception>? _errors;

        internal void Add(Exception error)
        {
            if (_errors == null)
            {
                _errors = new List<Exception>();
            }

            if (error is AggregateException aggregate)
            {
                _errors.AddRange(aggregate.Flatten().InnerExceptions);
            }
            else
            {
                _errors.Add(error);
            }
        }

        internal void ThrowIfAny()
        {
            if (_errors == null)
            {
                return;
            }

            if (_errors.Count == 1)
            {
                ExceptionDispatchInfo.Capture(_errors[0]).Throw();
            }

            throw new AggregateException("Cascade cleanup failed after all owned resources were visited.", _errors);
        }
    }
}
