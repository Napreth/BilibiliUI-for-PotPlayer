#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <libass/ass.h>

typedef struct { ASS_Library *library; ASS_Renderer *renderer; int fonts_dirty; } Context;
static void quiet(int level, const char *format, va_list args, void *data) { (void)level; (void)format; (void)args; (void)data; }
__declspec(dllexport) int __cdecl bili_ass_version(void) { return ass_library_version(); }
__declspec(dllexport) void __cdecl bili_ass_add_font(Context *ctx,const char *name,const char *data,int size)
{ if(ctx && name && data && size>0 && size<=16*1024*1024) { ass_add_font(ctx->library,name,data,size); ctx->fonts_dirty=1; } }
__declspec(dllexport) void *__cdecl bili_ass_create(void)
{
    Context *ctx = calloc(1,sizeof(*ctx));
    if (!ctx) return NULL;
    ctx->library = ass_library_init();
    if (!ctx->library) { free(ctx); return NULL; }
    ass_set_message_cb(ctx->library,quiet,NULL);
    ass_set_extract_fonts(ctx->library,1);
    ctx->renderer = ass_renderer_init(ctx->library);
    if (!ctx->renderer) { ass_library_done(ctx->library); free(ctx); return NULL; }
    ass_set_fonts(ctx->renderer,NULL,"Microsoft YaHei UI",ASS_FONTPROVIDER_DIRECTWRITE,NULL,1);
    ass_set_cache_limits(ctx->renderer,2000,32);
    return ctx;
}
__declspec(dllexport) void __cdecl bili_ass_destroy(Context *ctx)
{ if (ctx) { ass_renderer_done(ctx->renderer); ass_library_done(ctx->library); free(ctx); } }
__declspec(dllexport) void *__cdecl bili_ass_read(Context *ctx, const char *text, int length)
{
    if (!ctx || !text || length <= 0 || length > 32*1024*1024) return NULL;
    ASS_Track *track=ass_read_memory(ctx->library,(char *)text,length,"UTF-8");
    /* Embedded ASS fonts can arrive after a fallback font has been cached. */
    if (track) for (int row=0;row<length;) {
        int end=row; while (end<length && text[end]!='\n') end++;
        int first=row,last=end;
        while (first<last && (text[first]==' ' || text[first]=='\t')) first++;
        while (last>first && (text[last-1]=='\r' || text[last-1]==' ' || text[last-1]=='\t')) last--;
        if (last-first==7 && !_strnicmp(text+first,"[Fonts]",7)) { ctx->fonts_dirty=1; break; }
        row=end+1;
    }
    return track;
}
__declspec(dllexport) void __cdecl bili_ass_free_track(ASS_Track *track) { if (track) ass_free_track(track); }
__declspec(dllexport) int __cdecl bili_ass_regions(Context *ctx,ASS_Track *track,int64_t time,int width,int height,int *rects,int capacity)
{
    if (!ctx || !track || !rects || capacity<1 || width<1 || height<1 || width>16384 || height>16384) return 0;
    if (ctx->fonts_dirty) { ass_set_fonts(ctx->renderer,NULL,"Microsoft YaHei UI",ASS_FONTPROVIDER_DIRECTWRITE,NULL,1); ctx->fonts_dirty=0; }
    ass_set_frame_size(ctx->renderer,width,height); ass_set_storage_size(ctx->renderer,width,height);
    int changed,count=0;
    for (ASS_Image *image=ass_render_frame(ctx->renderer,track,time,&changed);image && count<capacity;image=image->next) {
        if ((image->color & 255)==255 || image->w<=0 || image->h<=0) continue;
        int l=image->dst_x<0?0:image->dst_x,t=image->dst_y<0?0:image->dst_y;
        int r=image->dst_x+image->w,b=image->dst_y+image->h;
        if (r>width) r=width; if (b>height) b=height;
        if (r<=l || b<=t) continue;
        rects[count*4]=l; rects[count*4+1]=t; rects[count*4+2]=r; rects[count*4+3]=b; count++;
    }
    return count;
}
__declspec(dllexport) int __cdecl bili_ass_render(Context *ctx, ASS_Track *track, int64_t time,
    int width, int height, int stride, unsigned char *pixels, int opacity)
{
    if (!ctx || !track || !pixels || width < 1 || height < 1 || width > 16384 || height > 16384 || stride < width*4) return -1;
    if (ctx->fonts_dirty) { ass_set_fonts(ctx->renderer,NULL,"Microsoft YaHei UI",ASS_FONTPROVIDER_DIRECTWRITE,NULL,1); ctx->fonts_dirty=0; }
    ass_set_frame_size(ctx->renderer,width,height);
    ass_set_storage_size(ctx->renderer,width,height);
    int changed, count = 0;
    ASS_Image *image = ass_render_frame(ctx->renderer,track,time,&changed);
    for (; image; image=image->next) {
        unsigned r=image->color>>24, g=image->color>>16 & 255, b=image->color>>8 & 255;
        unsigned alpha=(255-(image->color & 255)) * (unsigned)(opacity < 0 ? 0 : opacity > 100 ? 100 : opacity) / 100;
        if (!alpha) continue;
        for (int y=0;y<image->h;y++) {
            int dy=image->dst_y+y; if (dy<0 || dy>=height) continue;
            for (int x=0;x<image->w;x++) {
                int dx=image->dst_x+x; if (dx<0 || dx>=width) continue;
                unsigned a=(image->bitmap[(size_t)y*image->stride+x] * alpha+127)/255;
                if (!a) continue;
                unsigned char *p=pixels+(size_t)dy*stride+dx*4;
                unsigned inv=255-a;
                /* Destination is premultiplied BGRA, as required by UpdateLayeredWindow.
                   ASS masks have a potentially unpadded last row; read only width. */
                p[0]=(b*a+p[0]*inv+127)/255; p[1]=(g*a+p[1]*inv+127)/255;
                p[2]=(r*a+p[2]*inv+127)/255; p[3]=a+(p[3]*inv+127)/255;
            }
        }
        count++;
    }
    return count;
}
