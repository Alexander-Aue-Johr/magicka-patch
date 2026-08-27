"""
LZX decompression algorithm for Xbox 360 XEX2 and XNB files.

A Python implementation of the LZX algorithm as used in Microsoft
Cabinet files and Xbox 360 XEX executables. Based on the public LZX
specification and the WinCE LZX decoder by KodaSec.

The XNB integration keeps the 32 KiB frame position across separately framed
calls. The original implementation is distributed under the MIT license in
LICENSE.lzx-decompress.txt.
"""

import struct

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

_NUM_CHARS = 256
_MIN_MATCH = 2
_NUM_PRIMARY_LENGTHS = 7
_SECONDARY_NUM_ELEMENTS = 249

_PRETREE_NUM = 20
_PRETREE_TABLEBITS = 6
_PRETREE_MAXSYMBOLS = 20
_PRETREE_MAX_CODEWORD = 16

_MAINTREE_TABLEBITS = 11
_MAINTREE_MAXSYMBOLS = _NUM_CHARS + (51 << 3)
_MAINTREE_MAX_CODEWORD = 16

_LENTREE_TABLEBITS = 10
_LENTREE_MAXSYMBOLS = _SECONDARY_NUM_ELEMENTS
_LENTREE_MAX_CODEWORD = 16

_ALIGNTREE_TABLEBITS = 7
_ALIGNTREE_MAXSYMBOLS = 8
_ALIGNTREE_MAX_CODEWORD = 8

_LENTABLE_SAFETY = 64

_BLOCKTYPE_VERBATIM = 1
_BLOCKTYPE_ALIGNED = 2
_BLOCKTYPE_UNCOMPRESSED = 3

_NUM_POSITION_SLOTS = {
    15: 30, 16: 32, 17: 34, 18: 36, 19: 38, 20: 42, 21: 50,
}

_POSITION_BASE = [
    0, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192,
    256, 384, 512, 768, 1024, 1536, 2048, 3072, 4096, 6144, 8192,
    12288, 16384, 24576, 32768, 49152, 65536, 98304, 131072, 196608,
    262144, 393216, 524288, 655360, 786432, 917504, 1048576, 1179648,
    1310720, 1441792, 1572864, 1703936, 1835008, 1966080, 2097152,
]

_EXTRA_BITS = [
    0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
    7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14,
    15, 15, 16, 16, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17, 17,
    17, 17, 17, 17,
]


# ---------------------------------------------------------------------------
# Bitstream reader (matches the WinCE reference implementation)
# ---------------------------------------------------------------------------

class _BitBuffer:
    """
    LZX bitstream reader.

    Uses a plain Python integer as an *unbounded* bit buffer to avoid
    overflow issues that arise with a fixed-width (32-bit) ctypes buffer.
    Bits are stored MSB-first: the most-recently-consumed bit is always at
    the top of the integer, and new 16-bit LE words are appended below.
    """
    __slots__ = ('buf', 'bits_left', 'data', 'pos')

    def __init__(self, data):
        self.buf = 0
        self.bits_left = 0
        self.data = data
        self.pos = 0

    def ensure_bits(self, n):
        while self.bits_left < n:
            if self.pos + 1 < len(self.data):
                lo = self.data[self.pos]
                hi = self.data[self.pos + 1]
                self.pos += 2
            elif self.pos < len(self.data):
                lo = self.data[self.pos]
                hi = 0
                self.pos += 1
            else:
                lo = hi = 0
            word = (hi << 8) | lo
            self.buf = (self.buf << 16) | word
            self.bits_left += 16

    def peek_bits(self, n):
        return (self.buf >> (self.bits_left - n)) & ((1 << n) - 1)

    def remove_bits(self, n):
        self.bits_left -= n
        self.buf &= (1 << self.bits_left) - 1

    def read_bits(self, n):
        if n == 0:
            return 0
        self.ensure_bits(n)
        val = self.peek_bits(n)
        self.remove_bits(n)
        return val

    def reset(self):
        self.buf = 0
        self.bits_left = 0


# ---------------------------------------------------------------------------
# Huffman table builder and decoder (matches the WinCE reference)
# ---------------------------------------------------------------------------

def _make_decode_table(nsyms, nbits, length, table):
    """
    Build a Huffman decode table.

    This uses the same algorithm as the WinCE LZX reference implementation:
    short codes fill contiguous ranges in the direct table, and long codes
    use a linked binary tree.
    """
    pos = 0
    table_mask = 1 << nbits
    bit_mask = table_mask >> 1
    next_symbol = bit_mask

    # Fill table for codes of length 1..nbits
    for bit_num in range(1, nbits + 1):
        for sym in range(nsyms):
            if length[sym] == bit_num:
                leaf = pos
                pos += bit_mask
                if pos > table_mask:
                    return False
                for k in range(bit_mask):
                    table[leaf + k] = sym
        bit_mask >>= 1

    # If table is full, we're done
    if pos == table_mask:
        return True

    # Fill empty entries
    for sym in range(pos, table_mask):
        table[sym] = 0

    # Handle codes longer than nbits via binary tree
    pos <<= 16
    table_mask <<= 16
    bit_mask = 1 << 15

    for bit_num in range(nbits + 1, 17):
        for sym in range(nsyms):
            if length[sym] == bit_num:
                leaf = pos >> 16
                for _ in range(bit_num - nbits):
                    if table[leaf] == 0:
                        table[next_symbol << 1] = 0
                        table[(next_symbol << 1) + 1] = 0
                        table[leaf] = next_symbol
                        next_symbol += 1
                    leaf = table[leaf] << 1
                    if ((pos >> (15 - _)) & 1) == 1:
                        leaf += 1
                table[leaf] = sym
                pos += bit_mask
                if pos > table_mask:
                    return False
        bit_mask >>= 1

    if pos == table_mask:
        return True

    # Check if all lengths are zero (empty tree is OK)
    for sym in range(nsyms):
        if length[sym] != 0:
            return False
    return True


def _read_huff_sym(table, lengths, nsyms, nbits, bb, max_codeword):
    """Decode a single Huffman symbol from the bitstream."""
    bb.ensure_bits(max_codeword)
    i = table[bb.peek_bits(nbits)]
    if i >= nsyms:
        # Tree walk for long codes: check bits below the initial nbits
        bit_pos = bb.bits_left - nbits - 1
        while True:
            i <<= 1
            if bit_pos < 0:
                return 0
            if (bb.buf >> bit_pos) & 1:
                i |= 1
            bit_pos -= 1
            i = table[i]
            if i < nsyms:
                break
    bb.remove_bits(lengths[i])
    return i


# ---------------------------------------------------------------------------
# LZX Decoder
# ---------------------------------------------------------------------------

class LZXDecoder:
    """
    Stateful LZX decompressor.

    Maintains sliding window and Huffman tree state. Used for XEX2
    normal compression where the entire compressed stream is passed
    as one call.

    Usage::

        decoder = LZXDecoder(window_bits=15)
        result = decoder.decompress(compressed_data, output_size)
    """

    def __init__(self, window_bits):
        if window_bits not in _NUM_POSITION_SLOTS:
            raise ValueError(f"window_bits must be 15-21, got {window_bits}")

        self.window_bits = window_bits
        self.window_size = 1 << window_bits
        self.num_position_slots = _NUM_POSITION_SLOTS[window_bits]
        self.main_elements = _NUM_CHARS + (self.num_position_slots << 3)

        # Sliding window (initialized to 0xDC like the reference)
        self.window = bytearray(b'\xDC' * self.window_size)
        self.window_pos = 0

        # Repeated match offsets
        self.R0 = 1
        self.R1 = 1
        self.R2 = 1

        # Huffman tables and lengths
        self.pretree_table = [0] * ((1 << _PRETREE_TABLEBITS) + (_PRETREE_MAXSYMBOLS << 1))
        self.pretree_len = [0] * (_PRETREE_MAXSYMBOLS + _LENTABLE_SAFETY)

        self.maintree_table = [0] * ((1 << _MAINTREE_TABLEBITS) + (_MAINTREE_MAXSYMBOLS << 1))
        self.maintree_len = [0] * (_MAINTREE_MAXSYMBOLS + _LENTABLE_SAFETY)

        self.lentree_table = [0] * ((1 << _LENTREE_TABLEBITS) + (_LENTREE_MAXSYMBOLS << 1))
        self.lentree_len = [0] * (_LENTREE_MAXSYMBOLS + _LENTABLE_SAFETY)

        self.aligntree_table = [0] * ((1 << _ALIGNTREE_TABLEBITS) + (_ALIGNTREE_MAXSYMBOLS << 1))
        self.aligntree_len = [0] * (_ALIGNTREE_MAXSYMBOLS + _LENTABLE_SAFETY)

        # Block state
        self.block_remaining = 0
        self.block_length = 0
        self.block_type = 0

        # E8 translation
        self.header_read = False
        self.intel_filesize = 0
        self.intel_curpos = 0
        self.intel_started = False

    def _read_lengths(self, lens, first, last, bb):
        """Read Huffman code lengths using pretree encoding."""
        for i in range(_PRETREE_NUM):
            self.pretree_len[i] = bb.read_bits(4)

        _make_decode_table(_PRETREE_MAXSYMBOLS, _PRETREE_TABLEBITS,
                           self.pretree_len, self.pretree_table)

        x = first
        while x < last:
            z = _read_huff_sym(self.pretree_table, self.pretree_len,
                               _PRETREE_MAXSYMBOLS, _PRETREE_TABLEBITS,
                               bb, _PRETREE_MAX_CODEWORD)

            if z == 17:
                y = bb.read_bits(4) + 4
                for _ in range(y):
                    lens[x] = 0
                    x += 1
            elif z == 18:
                y = bb.read_bits(5) + 20
                for _ in range(y):
                    lens[x] = 0
                    x += 1
            elif z == 19:
                y = bb.read_bits(1) + 4
                z = _read_huff_sym(self.pretree_table, self.pretree_len,
                                   _PRETREE_MAXSYMBOLS, _PRETREE_TABLEBITS,
                                   bb, _PRETREE_MAX_CODEWORD)
                z = (lens[x] + 17 - z) % 17
                for _ in range(y):
                    lens[x] = z
                    x += 1
            else:
                z = (lens[x] + 17 - z) % 17
                lens[x] = z
                x += 1

    def _read_block_header(self, bb):
        """Read an LZX block header and build decoding tables."""
        # Handle uncompressed block alignment
        if self.block_type == _BLOCKTYPE_UNCOMPRESSED:
            if (self.block_length & 1) == 1:
                bb.pos += 1  # skip padding byte
            bb.reset()

        self.block_type = bb.read_bits(3)
        self.block_length = bb.read_bits(24)
        self.block_remaining = self.block_length

        if self.block_type == _BLOCKTYPE_ALIGNED:
            for i in range(8):
                self.aligntree_len[i] = bb.read_bits(3)
            _make_decode_table(_ALIGNTREE_MAXSYMBOLS, _ALIGNTREE_TABLEBITS,
                               self.aligntree_len, self.aligntree_table)

        if self.block_type in (_BLOCKTYPE_VERBATIM, _BLOCKTYPE_ALIGNED):
            self._read_lengths(self.maintree_len, 0, _NUM_CHARS, bb)
            self._read_lengths(self.maintree_len, _NUM_CHARS,
                               self.main_elements, bb)
            _make_decode_table(_MAINTREE_MAXSYMBOLS, _MAINTREE_TABLEBITS,
                               self.maintree_len, self.maintree_table)

            if self.maintree_len[0xE8] != 0:
                self.intel_started = True

            self._read_lengths(self.lentree_len, 0,
                               _SECONDARY_NUM_ELEMENTS, bb)
            _make_decode_table(_LENTREE_MAXSYMBOLS, _LENTREE_TABLEBITS,
                               self.lentree_len, self.lentree_table)

        elif self.block_type == _BLOCKTYPE_UNCOMPRESSED:
            self.intel_started = True
            bb.ensure_bits(16)
            if bb.bits_left > 16:
                bb.pos -= 2
            bb.reset()
            # Read R0, R1, R2 as 32-bit little-endian
            if bb.pos + 12 <= len(bb.data):
                self.R0 = (bb.data[bb.pos] | (bb.data[bb.pos+1] << 8) |
                           (bb.data[bb.pos+2] << 16) | (bb.data[bb.pos+3] << 24))
                bb.pos += 4
                self.R1 = (bb.data[bb.pos] | (bb.data[bb.pos+1] << 8) |
                           (bb.data[bb.pos+2] << 16) | (bb.data[bb.pos+3] << 24))
                bb.pos += 4
                self.R2 = (bb.data[bb.pos] | (bb.data[bb.pos+1] << 8) |
                           (bb.data[bb.pos+2] << 16) | (bb.data[bb.pos+3] << 24))
                bb.pos += 4
        else:
            raise ValueError(f"Invalid LZX block type: {self.block_type}")

    def decompress(self, data, output_size):
        """
        Decompress LZX compressed data.

        Faithfully mirrors mspack's lzxd.c frame-by-frame approach.
        A linear counter ``window_posn`` (not wrapped) tracks how many bytes
        have been decoded into the circular window.  ``frame_posn`` is the
        linear position where the current frame starts.  The formula
        ``bytes_todo = frame_posn + frame_size - window_posn`` naturally
        absorbs match overshoots: if the previous frame decoded N extra
        bytes, ``window_posn`` is N ahead, so ``bytes_todo`` is N less.

        Args:
            data: Compressed bytes (the entire concatenated stream).
            output_size: Expected decompressed size.

        Returns:
            Decompressed data as bytes.
        """
        bb = _BitBuffer(data)
        output = bytearray(output_size)
        out_pos = 0
        frame_size = 1 << 15               # 32 768 (LZX_FRAME_SIZE)
        window = self.window
        window_mask = self.window_size - 1

        # Use a LINEAR (non-wrapping) position counter for decode tracking.
        # The actual window index is always (window_posn & window_mask).
        window_posn = self.window_pos      # starts at 0
        # XNB stores each 32 KiB frame in a separate outer block and invokes
        # the stateful decoder once per block. Start at the current window
        # position instead of copying every call from window offset zero.
        frame_posn = window_posn

        # Read E8 translation header on first call
        if not self.header_read:
            intel_e8 = bb.read_bits(1)
            if intel_e8:
                hi16 = bb.read_bits(16)
                lo16 = bb.read_bits(16)
                self.intel_filesize = (hi16 << 16) | lo16
            self.intel_started = False
            self.header_read = True

        while out_pos < output_size:
            # Compute frame budget (may be smaller for the final frame)
            cur_frame_size = min(frame_size, output_size - out_pos)

            # How many bytes still need to be decoded for this frame.
            # If window_posn already advanced past frame_posn (overshoot
            # from the previous frame), bytes_todo is correspondingly less.
            bytes_todo = frame_posn + cur_frame_size - window_posn
            if bytes_todo < 0:
                bytes_todo = 0

            while bytes_todo > 0:
                # mspack uses == 0 (not <= 0).  A negative
                # block_remaining from a previous overshoot is
                # handled by the this_run assignment below, which
                # copies the negative value into this_run so that
                # bytes_todo gets the overshoot added back.
                if self.block_remaining == 0:
                    self._read_block_header(bb)

                this_run = self.block_remaining
                if this_run > bytes_todo:
                    this_run = bytes_todo
                bytes_todo -= this_run
                self.block_remaining -= this_run

                # When this_run is negative (from prior overshoot),
                # skip decoding -- the adjustments above restore
                # bytes_todo and zero out block_remaining.
                if this_run <= 0:
                    continue

                if self.block_type == _BLOCKTYPE_UNCOMPRESSED:
                    for _ in range(this_run):
                        b = bb.data[bb.pos] if bb.pos < len(bb.data) else 0
                        bb.pos += 1
                        window[window_posn & window_mask] = b
                        window_posn += 1

                else:
                    # Verbatim or Aligned block
                    while this_run > 0:
                        main_element = _read_huff_sym(
                            self.maintree_table, self.maintree_len,
                            _MAINTREE_MAXSYMBOLS, _MAINTREE_TABLEBITS,
                            bb, _MAINTREE_MAX_CODEWORD)

                        if main_element < _NUM_CHARS:
                            window[window_posn & window_mask] = main_element
                            window_posn += 1
                            this_run -= 1
                            continue

                        # Match
                        main_element -= _NUM_CHARS
                        match_length = main_element & _NUM_PRIMARY_LENGTHS
                        if match_length == _NUM_PRIMARY_LENGTHS:
                            length_footer = _read_huff_sym(
                                self.lentree_table, self.lentree_len,
                                _LENTREE_MAXSYMBOLS, _LENTREE_TABLEBITS,
                                bb, _LENTREE_MAX_CODEWORD)
                            match_length += length_footer
                        match_length += _MIN_MATCH

                        match_offset = main_element >> 3

                        if match_offset > 2:
                            extra = _EXTRA_BITS[match_offset]
                            if self.block_type == _BLOCKTYPE_ALIGNED and extra >= 3:
                                verbatim_bits = bb.read_bits(extra - 3)
                                verbatim_bits <<= 3
                                aligned_bits = _read_huff_sym(
                                    self.aligntree_table, self.aligntree_len,
                                    _ALIGNTREE_MAXSYMBOLS, _ALIGNTREE_TABLEBITS,
                                    bb, _ALIGNTREE_MAX_CODEWORD)
                            else:
                                verbatim_bits = bb.read_bits(extra)
                                aligned_bits = 0

                            match_offset = (_POSITION_BASE[match_offset]
                                            + verbatim_bits + aligned_bits - 2)
                            self.R2 = self.R1
                            self.R1 = self.R0
                            self.R0 = match_offset
                        elif match_offset == 0:
                            match_offset = self.R0
                        elif match_offset == 1:
                            match_offset = self.R1
                            self.R1 = self.R0
                            self.R0 = match_offset
                        else:
                            match_offset = self.R2
                            self.R2 = self.R0
                            self.R0 = match_offset

                        this_run -= match_length

                        # Copy match within window
                        runsrc = (window_posn - match_offset) & window_mask
                        for _ in range(match_length):
                            window[window_posn & window_mask] = window[runsrc]
                            window_posn += 1
                            runsrc = (runsrc + 1) & window_mask

                    # Charge any overshoot back to block_remaining
                    # (mspack: block_remaining -= -this_run)
                    if this_run < 0:
                        self.block_remaining -= -this_run

            # Re-align input bitstream to a 16-bit word boundary.
            # mspack does this at every frame boundary:
            #   if (bits_left > 0) ENSURE_BITS(16);
            #   if (bits_left & 15) REMOVE_BITS(bits_left & 15);
            if bb.bits_left > 0:
                bb.ensure_bits(16)
            if bb.bits_left & 15:
                bb.remove_bits(bb.bits_left & 15)

            # Copy cur_frame_size bytes from window to output
            wp = frame_posn & window_mask
            for _ in range(cur_frame_size):
                output[out_pos] = window[wp]
                wp = (wp + 1) & window_mask
                out_pos += 1

            # Advance frame position (linear, no wrapping needed;
            # window indices are always masked with window_mask)
            frame_posn += cur_frame_size

        # Store final position back
        self.window_pos = window_posn & window_mask

        # Apply Intel E8 translation
        if self.intel_started and output_size > 10:
            self._e8_decode(output, output_size)

        self.intel_curpos += output_size
        return bytes(output)

    def _e8_decode(self, data, size):
        """Undo Intel E8 x86 CALL translation."""
        if self.intel_curpos >= 0x40000000:
            return

        i = 0
        while i < size - 10:
            if data[i] != 0xE8:
                i += 1
                continue

            curpos = self.intel_curpos + i
            abs_off = (data[i+1] | (data[i+2] << 8) |
                       (data[i+3] << 16) | (data[i+4] << 24))
            if abs_off & 0x80000000:
                abs_off -= 0x100000000

            if abs_off >= -curpos and abs_off < self.intel_filesize:
                if abs_off >= 0:
                    rel_off = abs_off - curpos
                else:
                    rel_off = abs_off + self.intel_filesize
                rel_off &= 0xFFFFFFFF
                data[i+1] = rel_off & 0xFF
                data[i+2] = (rel_off >> 8) & 0xFF
                data[i+3] = (rel_off >> 16) & 0xFF
                data[i+4] = (rel_off >> 24) & 0xFF

            i += 5

    def reset(self):
        """Fully reset the decoder state."""
        self.__init__(self.window_bits)
