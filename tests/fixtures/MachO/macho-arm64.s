.section __TEXT,__text,regular,pure_instructions
.globl _add
_add:
  add w0, w0, w1
  ret
.globl _sub
_sub:
  sub w0, w0, w1
  ret
.section __DATA,__data
.globl _g
_g:
  .quad 42
.section __TEXT,__cstring,cstring_literals
_s:
  .asciz "hello-macho"
