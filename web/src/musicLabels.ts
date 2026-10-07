export const musicLabels: Record<string,string> = {
UniqueID:'歌曲 ID',MusicMessage:'歌曲名称',ArtistMessage:'艺术家',CopyrightMessage:'版权信息',VersionNo:'版本编号',AssetDirectory:'歌曲资源目录',MovieAssetName:'Normal 视频资源',MovieAssetNameHard:'Hard 视频资源',MovieAssetNameExpert:'Expert 视频资源',MovieAssetNameInferno:'Inferno 视频资源',JacketAssetName:'封面资源',Rubi:'检索读音',bRecommend:'推荐歌曲',WaccaPointCost:'WACCA 点数费用',bCollaboration:'联动标记',bWaccaOriginal:'原创标记',TrainingLevel:'训练等级',Reserved:'保留值',Bpm:'BPM',HashTag:'话题标签',PreviewBeginTime:'试听开始时间（秒）',PreviewSeconds:'试听时长（秒）',ScoreGenre:'歌曲分类',WorkBuffer:'内部缓冲值',AssetFullPath:'内部资源完整路径'
}
for (let i=0;i<10;i++) musicLabels[`MusicTagForUnlock${i}`] = `解锁标签 ${i}`
for (const difficulty of ['Normal','Hard','Extreme','Inferno']) {
 musicLabels[`Difficulty${difficulty}Lv`] = '难度等级'
 musicLabels[`ClearNormaRate${difficulty}`] = '通关标准比例'
}
for (const difficulty of ['Normal','Hard','Expert','Inferno']) musicLabels[`NotesDesigner${difficulty}`] = '谱面作者'
export function label(name:string) { return musicLabels[name] ?? name }
