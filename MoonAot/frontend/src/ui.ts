import type { DialogApi, MessageApi } from 'naive-ui'

let messageApi: MessageApi | null = null
let dialogApi: DialogApi | null = null

export function setUiApis(message: MessageApi, dialog: DialogApi) {
  messageApi = message
  dialogApi = dialog
}

export function toast(text: string, type: 'success' | 'info' | 'warning' | 'error' = 'success') {
  try { messageApi?.[type](text, { duration: 2600 }) } catch { }
}

export function toastError(error: unknown) {
  const msg = error instanceof Error ? error.message : String(error ?? '操作失败')
  try { messageApi?.error(msg, { duration: 4200 }) } catch { }
}

export function confirmDialog(options: {
  title: string
  content?: string
  positiveText?: string
  negativeText?: string
  danger?: boolean
}): Promise<boolean> {
  if (!dialogApi) return Promise.resolve(false)
  return new Promise<boolean>((resolve) => {
    let settled = false
    const done = (v: boolean) => { if (!settled) { settled = true; resolve(v) } }
    dialogApi!.warning({
      title: options.title,
      content: options.content,
      positiveText: options.positiveText ?? '确定',
      negativeText: options.negativeText ?? '取消',
      onPositiveClick: () => done(true),
      onNegativeClick: () => done(false),
      onClose: () => done(false),
      onMaskClick: () => done(false),
    })
  })
}
