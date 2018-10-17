// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using EditorBrowsableAttribute = System.ComponentModel.EditorBrowsableAttribute;
using EditorBrowsableState = System.ComponentModel.EditorBrowsableState;

using Internal.Runtime.CompilerServices;

namespace System
{
    /// <summary>
    /// Memory represents a contiguous region of arbitrary memory similar to <see cref="Span{T}"/>.
    /// Unlike <see cref="Span{T}"/>, it is not a byref-like type.
    /// </summary>
    [DebuggerTypeProxy(typeof(MemoryDebugView<>))]
    [DebuggerDisplay("{ToString(),raw}")]
    public readonly struct Memory<T>
    {
        // NOTE: With the current implementation, Memory<T> and ReadOnlyMemory<T> must have the same layout,
        // as code uses Unsafe.As to cast between them.

        // The highest order bit of _index is used to discern whether _object is a pre-pinned array.
        // (_index < 0) => _object is a pre-pinned array, so Pin() will not allocate a new GCHandle
        //       (else) => Pin() needs to allocate a new GCHandle to pin the object.
        private readonly object _object;
        private readonly int _index;
        private readonly int _length;

        private const int RemoveFlagsBitMask = 0x7FFFFFFF;

        /// <summary>
        /// Creates a new memory over the entirety of the target array.
        /// </summary>
        /// <param name="array">The target array.</param>
        /// <remarks>Returns default when <paramref name="array"/> is null.</remarks>
        /// <exception cref="System.ArrayTypeMismatchException">Thrown when <paramref name="array"/> is covariant and array's type is not exactly T[].</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory(T[] array)
        {
            if (array == null)
            {
                this = default;
                return; // returns default
            }
            if (default(T) == null && array.GetType() != typeof(T[]))
                ThrowHelper.ThrowArrayTypeMismatchException();

            _object = array;
            _index = 0;
            _length = array.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Memory(T[] array, int start)
        {
            if (array == null)
            {
                if (start != 0)
                    ThrowHelper.ThrowArgumentOutOfRangeException();
                this = default;
                return; // returns default
            }
            if (default(T) == null && array.GetType() != typeof(T[]))
                ThrowHelper.ThrowArrayTypeMismatchException();
            if ((uint)start > (uint)array.Length)
                ThrowHelper.ThrowArgumentOutOfRangeException();

            _object = array;
            _index = start;
            _length = array.Length - start;
        }

        /// <summary>
        /// Creates a new memory over the portion of the target array beginning
        /// at 'start' index and ending at 'end' index (exclusive).
        /// </summary>
        /// <param name="array">The target array.</param>
        /// <param name="start">The index at which to begin the memory.</param>
        /// <param name="length">The number of items in the memory.</param>
        /// <remarks>Returns default when <paramref name="array"/> is null.</remarks>
        /// <exception cref="System.ArrayTypeMismatchException">Thrown when <paramref name="array"/> is covariant and array's type is not exactly T[].</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the specified <paramref name="start"/> or end index is not in the range (&lt;0 or &gt;=Length).
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory(T[] array, int start, int length)
        {
            if (array == null)
            {
                if (start != 0 || length != 0)
                    ThrowHelper.ThrowArgumentOutOfRangeException();
                this = default;
                return; // returns default
            }
            if (default(T) == null && array.GetType() != typeof(T[]))
                ThrowHelper.ThrowArrayTypeMismatchException();
            if ((uint)start > (uint)array.Length || (uint)length > (uint)(array.Length - start))
                ThrowHelper.ThrowArgumentOutOfRangeException();

            _object = array;
            _index = start;
            _length = length;
        }

        /// <summary>
        /// Creates a new memory from a memory manager that provides specific method implementations beginning
        /// at 0 index and ending at 'end' index (exclusive).
        /// </summary>
        /// <param name="manager">The memory manager.</param>
        /// <param name="length">The number of items in the memory.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the specified <paramref name="length"/> is negative.
        /// </exception>
        /// <remarks>For internal infrastructure only</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Memory(MemoryManager<T> manager, int length)
        {
            Debug.Assert(manager != null);

            if (length < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException();

            _object = manager;
            _index = 0;
            _length = length;
        }

        /// <summary>
        /// Creates a new memory from a memory manager that provides specific method implementations beginning
        /// at 'start' index and ending at 'end' index (exclusive).
        /// </summary>
        /// <param name="manager">The memory manager.</param>
        /// <param name="start">The index at which to begin the memory.</param>
        /// <param name="length">The number of items in the memory.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the specified <paramref name="start"/> or <paramref name="length"/> is negative.
        /// </exception>
        /// <remarks>For internal infrastructure only</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Memory(MemoryManager<T> manager, int start, int length)
        {
            Debug.Assert(manager != null);

            if (length < 0 || start < 0)
                ThrowHelper.ThrowArgumentOutOfRangeException();

            _object = manager;
            _index = start;
            _length = length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Memory(object obj, int start, int length)
        {
            // No validation performed; caller must provide any necessary validation.
            _object = obj;
            _index = start;
            _length = length;
        }

        /// <summary>
        /// Defines an implicit conversion of an array to a <see cref="Memory{T}"/>
        /// </summary>
        public static implicit operator Memory<T>(T[] array) => new Memory<T>(array);

        /// <summary>
        /// Defines an implicit conversion of a <see cref="ArraySegment{T}"/> to a <see cref="Memory{T}"/>
        /// </summary>
        public static implicit operator Memory<T>(ArraySegment<T> segment) => new Memory<T>(segment.Array, segment.Offset, segment.Count);

        /// <summary>
        /// Defines an implicit conversion of a <see cref="Memory{T}"/> to a <see cref="ReadOnlyMemory{T}"/>
        /// </summary>
        public static implicit operator ReadOnlyMemory<T>(Memory<T> memory) =>
            Unsafe.As<Memory<T>, ReadOnlyMemory<T>>(ref memory);

        /// <summary>
        /// Returns an empty <see cref="Memory{T}"/>
        /// </summary>
        public static Memory<T> Empty => default;

        /// <summary>
        /// The number of items in the memory.
        /// </summary>
        public int Length => _length;

        /// <summary>
        /// Returns true if Length is 0.
        /// </summary>
        public bool IsEmpty => _length == 0;

        /// <summary>
        /// For <see cref="Memory{Char}"/>, returns a new instance of string that represents the characters pointed to by the memory.
        /// Otherwise, returns a <see cref="string"/> with the name of the type and the number of elements.
        /// </summary>
        public override string ToString()
        {
            if (typeof(T) == typeof(char))
            {
                return (_object is string str) ? str.Substring(_index, _length) : Span.ToString();
            }
            else if (typeof(T) == typeof(Utf8Char))
            {
                // Note: We don't call ToString below if typeof(T) == typeof(byte), since we're trying to draw a distinction
                // between textual data and binary data (that may happen to represent textual data).
                return Span.ToString();
            }
            return string.Format("System.Memory<{0}>[{1}]", typeof(T).Name, _length);
        }

        /// <summary>
        /// Forms a slice out of the given memory, beginning at 'start'.
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the specified <paramref name="start"/> index is not in range (&lt;0 or &gt;=Length).
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<T> Slice(int start)
        {
            if ((uint)start > (uint)_length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.start);
            }

            // It is expected for _index + start to be negative if the memory is already pre-pinned.
            return new Memory<T>(_object, _index + start, _length - start);
        }

        /// <summary>
        /// Forms a slice out of the given memory, beginning at 'start', of given length
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="length">The desired length for the slice (exclusive).</param>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown when the specified <paramref name="start"/> or end index is not in range (&lt;0 or &gt;=Length).
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Memory<T> Slice(int start, int length)
        {
            if ((uint)start > (uint)_length || (uint)length > (uint)(_length - start))
            {
                ThrowHelper.ThrowArgumentOutOfRangeException();
            }

            // It is expected for _index + start to be negative if the memory is already pre-pinned.
            return new Memory<T>(_object, _index + start, length);
        }

        /// <summary>
        /// Returns a span from the memory.
        /// </summary>
        public unsafe Span<T> Span
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // This property getter has special support for returning a mutable Span<char> that wraps
                // an immutable String instance. This is obviously a dangerous feature and breaks type safety.
                // However, we need to handle the case where a ReadOnlyMemory<char> was created from a string
                // and then cast to a Memory<T>. Such a cast can only be done with unsafe or marshaling code,
                // in which case that's the dangerous operation performed by the dev, and we're just following
                // suit here to make it work as best as possible.

                ref T refToReturn = ref Unsafe.AsRef<T>(null);
                int lengthOfUnderlyingSpan = 0;

                // Copy this field into a local so that it can't change out from under us mid-operation.

                object tmpObject = _object;
                if (tmpObject != null)
                {
                    if (typeof(T) == typeof(char) && tmpObject.GetType() == typeof(string))
                    {
                        // Special-case string since it's the most common for ROM<char>.
                        refToReturn = ref Unsafe.As<char, T>(ref Unsafe.As<string>(tmpObject).GetRawStringData());
                        lengthOfUnderlyingSpan = Unsafe.As<string>(tmpObject).Length;
                    }
                    else if (MayRepresentUtf8 && tmpObject.GetType() == typeof(Utf8String))
                    {
                        // Special-case Utf8String if we may contain UTF-8 data.
                        refToReturn = ref Unsafe.As<byte, T>(ref Unsafe.As<Utf8String>(tmpObject).GetRawStringData());
                        lengthOfUnderlyingSpan = Unsafe.As<Utf8String>(tmpObject).Length;
                    }
                    else if (RuntimeHelpers.ObjectHasComponentSize(tmpObject))
                    {
                        // We know the object is not null, it's not a string, and it is variable-length. The only
                        // remaining option is for it to be a T[] (or a U[] which is blittable to T[], like int[]
                        // and uint[]). Otherwise somebody used private reflection to set this field, and we're not
                        // too worried about type safety violations at this point.
                        Debug.Assert(tmpObject is Array);
                        refToReturn = ref Unsafe.As<T[]>(tmpObject).GetRawSzArrayData();
                        lengthOfUnderlyingSpan = Unsafe.As<T[]>(tmpObject).Length;
                    }
                    else
                    {
                        // We know the object is not null, and it's not variable-length, so it must be a MemoryManager<T>.
                        // Otherwise somebody used private reflection to set this field, and we're not too worried about
                        // type safety violations at that point.
                        Debug.Assert(tmpObject is MemoryManager<T>);
                        Span<T> memoryManagerSpan = Unsafe.As<MemoryManager<T>>(tmpObject).GetSpan();
                        refToReturn = ref MemoryMarshal.GetReference(memoryManagerSpan);
                        lengthOfUnderlyingSpan = memoryManagerSpan.Length;
                    }

                    // If the Memory<T> or ReadOnlyMemory<T> instance is torn, this property getter has undefined behavior.
                    // We try to detect this condition and throw an exception, but it's possible that a torn struct might
                    // appear to us to be valid, and we'll return an undesired span. Such a span is always guaranteed at
                    // least to be in-bounds when compared with the original Memory<T> instance, so using the span won't
                    // AV the process.

                    int desiredStartIndex = _index & RemoveFlagsBitMask;
                    int desiredLength = _length;

                    if ((uint)desiredStartIndex > (uint)lengthOfUnderlyingSpan || (uint)desiredLength > (uint)(lengthOfUnderlyingSpan - desiredStartIndex))
                    {
                        ThrowHelper.ThrowArgumentOutOfRangeException();
                    }

                    refToReturn = ref Unsafe.Add(ref refToReturn, desiredStartIndex);
                    lengthOfUnderlyingSpan = desiredLength;
                }

                return new Span<T>(ref refToReturn, lengthOfUnderlyingSpan);
            }
        }

        /// <summary>
        /// Copies the contents of the memory into the destination. If the source
        /// and destination overlap, this method behaves as if the original values are in
        /// a temporary location before the destination is overwritten.
        ///
        /// <param name="destination">The Memory to copy items into.</param>
        /// <exception cref="System.ArgumentException">
        /// Thrown when the destination is shorter than the source.
        /// </exception>
        /// </summary>
        public void CopyTo(Memory<T> destination) => Span.CopyTo(destination.Span);

        /// <summary>
        /// Copies the contents of the memory into the destination. If the source
        /// and destination overlap, this method behaves as if the original values are in
        /// a temporary location before the destination is overwritten.
        ///
        /// <returns>If the destination is shorter than the source, this method
        /// return false and no data is written to the destination.</returns>
        /// </summary>
        /// <param name="destination">The span to copy items into.</param>
        public bool TryCopyTo(Memory<T> destination) => Span.TryCopyTo(destination.Span);

        /// <summary>
        /// Creates a handle for the memory.
        /// The GC will not move the memory until the returned <see cref="MemoryHandle"/>
        /// is disposed, enabling taking and using the memory's address.
        /// <exception cref="System.ArgumentException">
        /// An instance with nonprimitive (non-blittable) members cannot be pinned.
        /// </exception>
        /// </summary>
        public unsafe MemoryHandle Pin()
        {
            // Just like the Span property getter, we have special support for a mutable Memory<char>
            // that wraps an immutable String instance. This might happen if a caller creates an
            // immutable ROM<char> wrapping a String, then uses Unsafe.As to create a mutable M<char>.
            // This needs to work, however, so that code that uses a single Memory<char> field to store either
            // a readable ReadOnlyMemory<char> or a writable Memory<char> can still be pinned and
            // used for interop purposes.

            // It's possible that the below logic could result in an AV if the struct
            // is torn. This is ok since the caller is expecting to use raw pointers,
            // and we're not required to keep this as safe as the other Span-based APIs.

            object tmpObject = _object;
            if (tmpObject != null)
            {
                if (typeof(T) == typeof(char) && tmpObject.GetType() == typeof(string))
                {
                    GCHandle handle = GCHandle.Alloc(tmpObject, GCHandleType.Pinned);
                    ref char stringData = ref Unsafe.Add(ref Unsafe.As<string>(tmpObject).GetRawStringData(), _index);
                    return new MemoryHandle(Unsafe.AsPointer(ref stringData), handle);
                }
                else if (MayRepresentUtf8 && tmpObject.GetType() == typeof(Utf8String))
                {
                    GCHandle handle = GCHandle.Alloc(tmpObject, GCHandleType.Pinned);
                    ref byte stringData = ref Unsafe.Add(ref Unsafe.As<Utf8String>(tmpObject).GetRawStringData(), _index);
                    return new MemoryHandle(Unsafe.AsPointer(ref stringData), handle);
                }
                else if (tmpObject is T[] array)
                {
                    // Array is already pre-pinned
                    if (_index < 0)
                    {
                        void* pointer = Unsafe.Add<T>(Unsafe.AsPointer(ref array.GetRawSzArrayData()), _index & RemoveFlagsBitMask);
                        return new MemoryHandle(pointer);
                    }
                    else
                    {
                        GCHandle handle = GCHandle.Alloc(array, GCHandleType.Pinned);
                        void* pointer = Unsafe.Add<T>(Unsafe.AsPointer(ref array.GetRawSzArrayData()), _index);
                        return new MemoryHandle(pointer, handle);
                    }
                }
                else
                {
                    // Can't use Unsafe.As<MemoryManager<T>> below since tmpObject could be a U[], where
                    // U is blittable with T. (example: int[] and uint[])
                    return ((MemoryManager<T>)tmpObject).Pin(_index);
                }
            }

            return default;
        }

        /// <summary>
        /// Copies the contents from the memory into a new array.  This heap
        /// allocates, so should generally be avoided, however it is sometimes
        /// necessary to bridge the gap with APIs written in terms of arrays.
        /// </summary>
        public T[] ToArray() => Span.ToArray();

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// Returns true if the object is Memory or ReadOnlyMemory and if both objects point to the same array and have the same length.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override bool Equals(object obj)
        {
            if (obj is ReadOnlyMemory<T>)
            {
                return ((ReadOnlyMemory<T>)obj).Equals(this);
            }
            else if (obj is Memory<T> memory)
            {
                return Equals(memory);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if the memory points to the same array and has the same length.  Note that
        /// this does *not* check to see if the *contents* are equal.
        /// </summary>
        public bool Equals(Memory<T> other)
        {
            return
                _object == other._object &&
                _index == other._index &&
                _length == other._length;
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override int GetHashCode()
        {
            return _object != null ? CombineHashCodes(RuntimeHelpers.GetHashCode(_object), _index.GetHashCode(), _length.GetHashCode()) : 0;
        }

        private static int CombineHashCodes(int left, int right)
        {
            return ((left << 5) + left) ^ right;
        }

        private static int CombineHashCodes(int h1, int h2, int h3)
        {
            return CombineHashCodes(CombineHashCodes(h1, h2), h3);
        }

        // Returns true iff this Memory<T> / ROM<T> instance may represent UTF-8 data.
        // JIT should elide this entire method into a single true / false.
        private static bool MayRepresentUtf8 => typeof(T) == typeof(byte) || typeof(T) == typeof(Utf8Char);
    }
}