class UnsafePointer<T> {
    private view: DataView;
    private offset: number;
    private _read: (view: DataView, offset: number) => T;
    private _write: (view: DataView, offset: number, value: T) => void;
    private _size: number;

    /**
     * Constructs a new pointer to an ArrayBuffer.
     * @param buffer The underlying ArrayBuffer (simulated memory).
     * @param offset The byte offset into the buffer where this pointer points.
     * @param read Optional. A function to read a value of type T from the DataView.
     *              Defaults to reading a 64-bit float (for numbers).
     * @param write Optional. A function to write a value of type T into the DataView.
     *              Defaults to writing a 64-bit float.
     * @param size  Optional. The size (in bytes) that T occupies. Defaults to 8.
     */
    constructor(
        buffer: ArrayBuffer,
        offset: number,
        read?: (view: DataView, offset: number) => T,
        write?: (view: DataView, offset: number, value: T) => void,
        size?: number
    ) {
        this.view = new DataView(buffer);
        this.offset = offset;
        // For the default, assume T is a number and use 64-bit float.
        this._read = read ?? ((v, o) => v.getFloat64(o, true) as unknown as T);
        this._write =
            write ??
            ((v, o, val) => v.setFloat64(o, val as unknown as number, true));
        this._size = size ?? 8;
    }

    // Dereference to read the value stored at this pointer.
    get value(): T {
        return this._read(this.view, this.offset);
    }

    // Write a value to the location this pointer points to.
    set value(val: T) {
        this._write(this.view, this.offset, val);
    }

    // For debugging purposes, show the current offset.
    toString(): string {
        return `UnsafePointer(offset=${this.offset})`;
    }
}
