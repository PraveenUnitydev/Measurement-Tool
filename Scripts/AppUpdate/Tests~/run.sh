#!/bin/sh
# App update checks outside Unity (needs Mono; Node for the cross-check with VRSP's rules, if the VRSP repo is next to
# this one or VRSP_BACKEND points at its backend folder). Builds DASUpdater.exe into Updater~ as well.
set -e
cd "$(dirname "$0")"
OUT=$(mktemp -d)
mcs -nologo -target:winexe -out:../Updater~/DASUpdater.exe -r:System.Core ../Updater~/DasUpdater.cs ../AppReleaseCore.cs
mcs -nologo -out:"$OUT/tests.exe" -r:System.Core AppUpdateTests.cs ../AppReleaseCore.cs
VRSP_BACKEND="${VRSP_BACKEND:-../../../../my-new-repo/backend}"
if command -v node >/dev/null 2>&1 && [ -f "$VRSP_BACKEND/utils/dasAppRelease.js" ]; then
  # Node signs a release the way VRSP checks them; C# must accept it, and Node must accept C#'s
  node -e '
    const crypto=require("crypto"),fs=require("fs"),R=require(process.argv[1]+"/utils/dasAppRelease.js"),d=process.argv[2];
    const {publicKey,privateKey}=crypto.generateKeyPairSync("rsa",{modulusLength:2048});const j=publicKey.export({format:"jwk"});
    const b=(u)=>Buffer.from(u,"base64url").toString("base64");
    fs.writeFileSync(d+"/node-pub.xml",`<RSAKeyValue><Modulus>${b(j.n)}</Modulus><Exponent>${b(j.e)}</Exponent></RSAKeyValue>`);
    const sha=(s)=>crypto.createHash("sha256").update(s).digest("hex");
    const m=[{path:"DAS.exe",sha256:sha("exe"),size:3},{path:"Data/x y.dll",sha256:sha("x"),size:1}];
    fs.writeFileSync(d+"/node-sig.txt",crypto.sign("sha256",Buffer.from(R.canonical("1.0.0",m)),{key:privateKey,padding:crypto.constants.RSA_PKCS1_PADDING}).toString("base64"));
  ' "$VRSP_BACKEND" "$OUT"
  mono "$OUT/tests.exe" ../Updater~/DASUpdater.exe "$OUT"
  node -e '
    const fs=require("fs"),R=require(process.argv[1]+"/utils/dasAppRelease.js"),d=process.argv[2];
    const text=fs.readFileSync(d+"/cs-files.txt","utf8"),lines=text.split("\n").slice(2).filter(Boolean);
    const files=lines.map(l=>{const a=l.indexOf(" "),b=l.indexOf(" ",a+1);return{sha256:l.slice(0,a),size:+l.slice(a+1,b),path:l.slice(b+1)}});
    const same=R.canonical("1.0.0",files)===text, ok=R.verifySignature("1.0.0",files,fs.readFileSync(d+"/cs-sig.txt","utf8"),R.publicKeyFrom(fs.readFileSync(d+"/cs-pub.xml","utf8")));
    console.log(same&&ok?"Cross-check: VRSP accepts a release signed in C#: Passed 1 | Failed 0":"FAIL cross-check (same text "+same+", signature "+ok+")");
  ' "$VRSP_BACKEND" "$OUT"
else
  echo "(Node or the VRSP repo not found: skipping the cross-check with VRSP)"
  mono "$OUT/tests.exe" ../Updater~/DASUpdater.exe
fi
# AppUpdater.cs compiles against Unity stand-ins, and its plain parts work
mcs -nologo -out:"$OUT/runtime.exe" -r:System.Core RuntimeStubs.cs RuntimeTests.cs ../AppUpdater.cs ../AppReleaseCore.cs ../DasAppUpdateKey.cs
mono "$OUT/runtime.exe"
# End-to-end with VRSP's real controller over HTTP and the real DASUpdater.exe
if command -v node >/dev/null 2>&1 && [ -f "$VRSP_BACKEND/controllers/dasAppController.js" ] && [ -d "$VRSP_BACKEND/node_modules/express" ]; then
  if VRSP_BACKEND="$VRSP_BACKEND" node e2e/e2e.js > "$OUT/e2e.log" 2>&1; then grep "^End-to-end" "$OUT/e2e.log"; else cat "$OUT/e2e.log"; exit 1; fi
else
  echo "(VRSP repo with node_modules not found: skipping the end-to-end run)"
fi
rm -rf "$OUT"
