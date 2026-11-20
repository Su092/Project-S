"""Remove technical background with flood fill BEFORE taking the alpha bounding box.
Solid panels retain enclosed dark artwork. Hollow glyphs also clear enclosed black components.
"""
import numpy as np
from PIL import Image, ImageDraw

def extract(im, solid=False, glyph=False, background_tolerance=20):
 rgba=np.array(im.convert("RGBA"))
 rgb=rgba[:,:,:3]
 candidate=(rgb.max(axis=2)<=background_tolerance)|(rgba[:,:,3]==0)
 # Add an eligible one-pixel border so all four image edges have the same outside seed.
 flood=Image.fromarray(np.pad(candidate.astype("uint8")*255,1,constant_values=255)).copy()
 ImageDraw.floodfill(flood,(0,0),128,thresh=0)
 cleared=np.array(flood)[1:-1,1:-1]==128
 if not solid:
  # Each enclosed component of the same technical black is a glyph cutout, not a dark panel.
  cleared |= np.array(flood)[1:-1,1:-1]==255
 rgba[cleared,3]=0
 cleaned=Image.fromarray(rgba,"RGBA")
 bounds=cleaned.getchannel("A").getbbox()
 if bounds is None:raise ValueError("Empty component after background flood fill")
 # Only alpha decides the crop; thin/dark edge decorations are never threshold-cropped.
 return cleaned.crop(bounds)
