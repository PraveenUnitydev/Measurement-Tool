# Compile-checks a URP .shader outside Unity against the real URP ShaderLibrary, across keyword variants.
# Setup (once): git clone --depth 1 --filter=blob:none --sparse -b 6000.0/staging https://github.com/Unity-Technologies/Graphics.git ugfx
#   (cd ugfx && git sparse-checkout set --no-cone '/Packages/com.unity.render-pipelines.core/ShaderLibrary/' \
#        '/Packages/com.unity.render-pipelines.universal/ShaderLibrary/' '/Packages/com.unity.render-pipelines.universal-config/' '*.hlsl')
#   and get slangc (e.g. from the 'slangtorch' wheel). Put ugfx next to this script or edit U; set SLANGC.
# usage: API=D3D11 TARGET=hlsl python3 check_shader.py path/to/Shader.shader [random-variant-count]
#        API=VULKAN TARGET=spirv python3 check_shader.py ...
# Only errors in the shader's own code are reported (the URP library itself trips a few slang-vs-fxc differences).
# Compile each pass of a Unity URP .shader with glslang (HLSL -> SPIR-V) against the real URP 6000.0 ShaderLibrary.
import re,sys,os,subprocess,itertools,random
SC=os.path.dirname(os.path.abspath(__file__))
U=SC+'/ugfx'
SLANG=os.environ.get('SLANGC','/tmp/slx/slangtorch/bin/slangc')
API=os.environ.get('API','D3D11')
TARGET=os.environ.get('TARGET','dxbc' if API=='D3D11' else 'spirv')
src=open(sys.argv[1]).read()
PRE=''.join(f'typedef half{n} min16float{n};\ntypedef int{n} min16int{n};\ntypedef uint{n} min16uint{n};\n' for n in ['','2','3','4'])

sdir=os.path.dirname(os.path.abspath(sys.argv[1]))
inc=re.search(r'HLSLINCLUDE(.*?)ENDHLSL',src,re.S)
include=inc.group(1) if inc else ''
passes=re.findall(r'Name\s+"(\w+)".*?HLSLPROGRAM(.*?)ENDHLSL',src,re.S)
fails=0;total=0
for name,body in passes:
    code=include+'\n'+body
    vert=re.search(r'#pragma vertex (\w+)',code).group(1)
    frag=re.search(r'#pragma fragment (\w+)',code).group(1)
    kwsets=[]
    for m in re.finditer(r'#pragma (multi_compile|shader_feature)(?:_local)?(?:_fragment|_vertex)?\s+(.*)',code):
        if m.group(2).strip() in ('fog','instancing'):
            continue
        ks=m.group(2).split()
        if m.group(1)=='shader_feature' and len(ks)==1: ks=['_']+ks
        kwsets.append(ks)
    if 'multi_compile_fog' in code: kwsets.append(['_','FOG_LINEAR','FOG_EXP2'])
    pass  # instancing needs Unity platform macros
    if 'ProbeVolumeVariants' in code: kwsets.append(['_','PROBE_VOLUMES_L1','PROBE_VOLUMES_L2'])
    clean=re.sub(r'#pragma[^\n]*','',code).replace('#include_with_pragmas','#include')
    # variants: all-off, all-first, all-last, plus random samples
    combos=[tuple(k[0] for k in kwsets),tuple(k[-1] for k in kwsets),tuple(k[min(1,len(k)-1)] for k in kwsets)]
    random.seed(1)
    for _ in range(int(sys.argv[2]) if len(sys.argv)>2 else 12): combos.append(tuple(random.choice(k) for k in kwsets))
    for combo in dict.fromkeys(combos):
        defs=[c for c in combo if c!='_']
        for stage,entry,flag in (('vert',vert,'SHADER_STAGE_VERTEX'),('frag',frag,'SHADER_STAGE_FRAGMENT')):
            total+=1
            f=f'{SC}/_pass.hlsl'; open(f,'w').write(PRE+clean)
            cmd=[SLANG,f,'-lang','hlsl','-stage',{'vert':'vertex','frag':'fragment'}[stage],'-entry',entry,'-target',TARGET,'-I',U,'-I',sdir,
                 '-DSHADER_API_'+API+'=1','-DSHADER_TARGET=45','-DUNITY_VERSION=60000063','-D'+flag+'=1','-o','/dev/null']
            cmd+=['-D'+d+'=1' for d in defs]
            r=subprocess.run(cmd,capture_output=True,text=True)
            if r.returncode!=0:
                L=(r.stdout+r.stderr).splitlines(); errs=[]
                for i,l in enumerate(L):
                    if l.startswith('error'):
                        loc=L[i+1].strip() if i+1<len(L) else ''
                        msg=next((x.split('^')[-1].strip() for x in L[i+1:i+8] if '^' in x),'')
                        errs.append((l+' '+loc+' :: '+msg).replace(U,'U').replace(sdir,'S'))
                allerr=list(dict.fromkeys(errs))
                mine=[e for e in allerr if '_pass.hlsl' in e or 'S/' in e or 'token paste' in e]
                if os.environ.get('ALL'): mine=allerr[:8]
                if mine:
                    fails+=1
                    print(f'FAIL {name} {stage} {defs}\n  '+'\n  '.join(mine[:8]))
print(f'compiled {total-fails}/{total} pass-stage variants')
sys.exit(1 if fails else 0)
