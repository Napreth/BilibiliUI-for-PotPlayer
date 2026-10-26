#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#define ZSTD_STATIC_LINKING_ONLY
#include "zstd.h"
#include "brotli/decode.h"

#define MAX_BYTES (32U * 1024U * 1024U)
#define MAX_ALLOC (64U * 1024U * 1024U)
typedef struct { size_t used; } Budget;
typedef union { size_t size; max_align_t alignment; } Block;
static void *limited_alloc(void *opaque, size_t size) {
    Budget *budget = opaque;
    if (size > MAX_ALLOC - budget->used) return NULL;
    Block *block = malloc(sizeof(Block) + size);
    if (!block) return NULL;
    block->size = size; budget->used += size;
    return block + 1;
}
static void limited_free(void *opaque, void *address) {
    if (!address) return;
    Block *block = (Block *)address - 1;
    ((Budget *)opaque)->used -= block->size; free(block);
}
/* Only bytes are decompressed. No file access, scripting or payload execution. */
__declspec(dllexport) int __cdecl bili_raw_decompress(int codec, const uint8_t *input, int length, uint8_t *output, int capacity) {
    if (!input || !output || length <= 0 || length > MAX_BYTES || capacity <= 0 || capacity > MAX_BYTES) return -1;
    Budget budget = {0};
    if (codec == 1) {
        ZSTD_customMem memory = { limited_alloc, limited_free, &budget };
        ZSTD_DCtx *decoder = ZSTD_createDCtx_advanced(memory);
        if (!decoder) return -1;
        size_t size = ZSTD_decompressDCtx(decoder, output, (size_t)capacity, input, (size_t)length);
        ZSTD_freeDCtx(decoder);
        return ZSTD_isError(size) || size > (size_t)capacity ? -1 : (int)size;
    }
    if (codec == 2) {
        BrotliDecoderState *decoder = BrotliDecoderCreateInstance(limited_alloc, limited_free, &budget);
        if (!decoder) return -1;
        size_t available_in = (size_t)length, available_out = (size_t)capacity;
        const uint8_t *next_in = input; uint8_t *next_out = output;
        BrotliDecoderResult result = BrotliDecoderDecompressStream(decoder, &available_in, &next_in, &available_out, &next_out, NULL);
        BrotliDecoderDestroyInstance(decoder);
        return result == BROTLI_DECODER_RESULT_SUCCESS && available_in == 0 ? capacity - (int)available_out : -1;
    }
    return -1;
}
