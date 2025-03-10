/*
 * Copyright (C) 2014 Patrick Mours
 * SPDX-License-Identifier: BSD-3-Clause
 */

#include "d3d9_impl_state_block.hpp"

reshade::d3d9::state_block::state_block(IDirect3DDevice9 *device) :
	_device(device)
{
#ifdef RESHADE_TEST_APPLICATION
	// Avoid errors from the D3D9 debug runtime because the other slots return D3DERR_NOTFOUND with the test application
	_num_simultaneous_rts = 1;
#else
	D3DCAPS9 caps;
	_device->GetDeviceCaps(&caps);
	_num_simultaneous_rts = caps.NumSimultaneousRTs;
	if (_num_simultaneous_rts > ARRAYSIZE(_render_targets))
		_num_simultaneous_rts = ARRAYSIZE(_render_targets);
#endif
}
reshade::d3d9::state_block::~state_block()
{
	release_all_device_objects();
}

void reshade::d3d9::state_block::capture()
{
	assert(!has_captured());

	if (SUCCEEDED(_device->CreateStateBlock(D3DSBT_ALL, &_state_block)))
		_state_block->Capture();

	_device->GetViewport(&_viewport);

	for (DWORD target = 0; target < _num_simultaneous_rts; target++)
		_device->GetRenderTarget(target, &_render_targets[target]);
	_device->GetDepthStencilSurface(&_depth_stencil);
}
void reshade::d3d9::state_block::apply_and_release()
{
	if (_state_block != nullptr)
		_state_block->Apply();

	// Release state block every time, so that all references to captured vertex and index buffers, textures, etc. are released again
	_state_block.reset();

	for (DWORD target = 0; target < _num_simultaneous_rts; target++)
		_device->SetRenderTarget(target, _render_targets[target].get());
	_device->SetDepthStencilSurface(_depth_stencil.get());

	// Set viewport after render targets have been set, since 'SetRenderTarget' causes the viewport to be set to the full size of the render target
	_device->SetViewport(&_viewport);

	release_all_device_objects();
}

void reshade::d3d9::state_block::release_all_device_objects()
{
	_depth_stencil.reset();
	for (auto &render_target : _render_targets)
		render_target.reset();
}
