.section __TEXT,__text,regular,pure_instructions
.globl _compute
.p2align 2
_compute:
  add   x0, x1, x2
  sub   w3, w4, w5
  adds  x6, x7, x8
  subs  w9, w10, w11
  and   x12, x13, x14
  orr   x15, x16, x17
  eor   w18, w19, w20
  mov   x21, x22
  movz  x23, #0x1234
  movk  x23, #0x5678, lsl #16
  lsl   w24, w25, #3
  lsr   x26, x27, #5
  asr   w28, w29, #2
  cmp   x0, x1
  cmn   w2, w3
  tst   x4, x5
  mul   x6, x7, x8
  madd  x9, x10, x11, x12
  sdiv  x13, x14, x15
  udiv  w16, w17, w18
  ldr   x0, [x1]
  str   w2, [x3, #8]
  ldp   x4, x5, [x6]
  stp   x7, x8, [x9, #16]
  ldrb  w10, [x11]
  strh  w12, [x13]
  adr   x14, _compute
  adrp  x15, _compute@PAGE
  csel  x16, x17, x18, eq
  cset  w19, ne
  sbfx  x20, x21, #4, #8
  ubfx  w22, w23, #2, #6
  bfi   x24, x25, #1, #3
  neg   x26, x27
  cinc  w28, w29, lt
  nop
  cbz   x0, _end
  cbnz  w1, _end
  tbz   x2, #3, _end
  bl    _end
  b.eq  _end
_end:
  ret
