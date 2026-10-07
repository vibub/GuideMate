const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

async function run(cookies) {
  let handler, outgoing, query;
  const chrome = {
    cookies: {getAll: async value => {query=value;return cookies;}},
    runtime: {id:'test-extension', onMessage:{addListener:value=>handler=value},
      sendNativeMessage:async (host,value)=>{outgoing={host,value};return {success:true,message:'ok'};}}
  };
  vm.runInNewContext(fs.readFileSync('chrome-extension/service-worker.js','utf8'),{chrome,Date});
  const reply=await new Promise(resolve=>handler({type:'sync-bilibili',pairingCode:'A'.repeat(32)},{id:'test-extension'},resolve));
  return {reply,outgoing,query,handler};
}
(async()=>{
  const fake={name:'SESSDATA',value:'synthetic-test-only',domain:'.bilibili.com',path:'/',session:true,secure:true,httpOnly:true,sameSite:'lax'};
  const result=await run([fake,{...fake,domain:'.bilibili.com.attacker.test'},
    {...fake,domain:'.other.test'}, {...fake,name:'partitioned',partitionKey:{topLevelSite:'https://bilibili.com'}},
    {...fake,name:'expired',session:false,expirationDate:1}]);
  assert.equal(result.query.domain,'bilibili.com');
  assert.equal(result.outgoing.value.cookies.length,1);
  assert.equal(result.outgoing.value.skippedPartitioned,1);
  assert.equal(result.outgoing.host,'com.guidemate.bilibili');
  assert.equal(result.outgoing.value.cookies[0].httpOnly,true);
  assert.equal(result.reply.success,true);
  assert.equal(result.handler({type:'sync-bilibili',pairingCode:'A'.repeat(32)},{id:'foreign'},()=>{}),false);
  assert.equal(result.handler({type:'sync-bilibili',pairingCode:'bad'},{id:'test-extension'},()=>{}),false);
  const loggedOut=await run([]);assert.equal(loggedOut.reply.success,false);assert.equal(loggedOut.outgoing,undefined);
  console.log('PASS Chrome scope, attributes, partitioned/expired filtering, sender, pairing and logged-out checks');
})().catch(error=>{console.error(error);process.exitCode=1;});
